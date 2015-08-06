# Logging using ETW and EventSource

This project aims to provide a suite of tools for using .NET's [EventSource](https://msdn.microsoft.com/en-us/library/system.diagnostics.tracing.eventsource%28v=vs.110%29.aspx?f=255&MSPPError=-2147217396)
to perform logging within applications. Along with systems for logging to a variety of destinations (memory,
console, disk, network) tools are provided which wrap [TraceEvent](http://blogs.msdn.com/b/dotnet/archive/2013/08/15/announcing-traceevent-monitoring-and-diagnostics-for-the-cloud.aspx)
to provide a streamlined experience for parsing ETW data both from disk and realtime sessions.

Additional documentation is available in the [doc](tree/master/doc) directory.

## What's In the Box

The core library provides the following major types to facilitate reading and writing logs:
* LogManager: The core type used for managing one or more log destinations. It can either be provided with
  XML-based configuration or controlled through sets of APIs to create, query, and tear down logging
  sessions. Available facilities include:
  * Activity ID management (setting ETW activity IDs)
  * Configuration management (providing either configuration strings or files with configuration contents).
  * Session management (creating, querying, and destroying individual logging sessions).
* ETWfileProcessor, ETWRealtimeProcessor: Provide facilities for reading event streams from either a
  an existing file or a realtime listening session. All events are wrapped in `ETWEvent` objects which
  provide wrappers to the objects presented by TraceEvent with a focus on making uniform access to named
  parameters for an event trivial no matter the source of the ETW event.
* ExpiringCompositeEventCollection: Provides a wrapper type for building complex views on top of many ETW
  events with a common key (e.g. the same ActivityID). This type helps by handling 'expiration' of multi-event
  data by allowing the defined type to declare when it is complete and, even in the case of a lossy event
  stream, expiring out events which never completed after a given period.

## Logging Philosophy

The below is sampled from the original documentation within Bing for this library and explains a bit of why
this method was chosen for this logging implementation.

In many applications logging is treated as an infrequent activity composed of complex monolithic log
statements containing large amounts of disparate data. A typical example would be a statement like
`Processed and responded to request <x> in time <y> with <z> byte response.` Within this statement
three pieces of crucial data exist, along with an array of metadata about what happened (`Processed and 
responded`, along with the actual meanings of x, y, and z.)

However, this method of logging presents some challenges and opportunities for improvement. During
application debugging it is often very helpful to have a timeline of detailed events. While it is possible to
reverse engineer this timeline from a small set of monolithic statements it can be difficult, and some
information must either be preserved for the duration of the timeline or lost. For example, in order to
provide `time <y>` `above you must keep a start time for the action. You may also wish to know what
thread(s) have touched a request, or other additional information. You must then store all this data and
emit it in a fashion that allows you to reassemble the timeline of activity.

In contrast to the monolithic approach it is possible to emit a small message relating to very small and
specific actions and combine those actions into a summary or timeline of the activity. This is relatively
simple given a simple common key (for example a GUID) across all actions common to an overall activity.
This also provides a mechanism for "filtering" the timeline for only specific actions of interest (perhaps
you do not care about the size of the reply, or perhaps you do not care about the duration of the activity.)
This library provides a facility for simplifying this through the `ExpiringCompositiveEventCollection` type.

The drawback to writing atomic events has always been the cost of writing many events. This cost manifests
both as a runtime cost (each event imposes an I/O penalty) and a size cost (it is usually more
space-efficient to write the monolithic event with a single copy of all the pertinent data.) However, on
modern hardware with advanced systems the absolute cost becomes negligible. In particular on consumer-grade
hardware ETW has proven itself capable of writing over 500,000 distinct events per second with minimal
penalties to the process performing the writing and excellent performance.

Given this we are choosing to follow a model of many atomic events with offline processing to "join" them
as needed. So while the typical logging practice has long been collect-and-write we are encouraging users
to split their activities into individual actions and write them as they occur.

## ETW Primer

This [MSDN Magazine article](http://msdn.microsoft.com/en-us/magazine/cc163437.aspx) presents a nice
in-depth overview of ETW. Take a look at it, there's an abbreviated summary here as well.

ETW (Event Tracing for Windows) is an eventing/tracing system composed of providers and trace sessions.
Providers emit lightly schematized events composed of zero or more pieces of atomic data (strings, integers),
and each event is marked up with metadata describing its severity and 'keywords' (in the form of distinct
bits in a 64 bit integer) to help categorize events. Sessions subscribe to events from one or more providers,
with per-provider filters on the aforementioned severity and keywords.

### Providers

Every ETW provider is uniquely identified by a GUID. The GUID is how providers are subscribed to in tracing
sessions, and must be registered with the system when the provider is available.

Within a provider one or more events may be emitted. These events are described by a
[manifest](http://msdn.microsoft.com/en-us/library/windows/desktop/dd996930%28=vs.85%29.aspx) which describes
the provider as a whole and its individual events.

Individual events definitions consist of the below information. Some additional metadata has been left off
this list as it is not commonly used in modern ETW scenarios.

* An ID from 1 - 65535 (unsigned 16 bit integer)
* The severity of the event (verbose, informational, warning, error, or critical)
* A 64 bit field in which each bit is a 'keyword' used for categorizing the event. An event may have 0 or
  more keywords. The upper 16 bits are considered reserved for the operating system, providing an effective
  per-provider space of 48 keywords.
* An optional 8 bit 'Opcode' value that can be used to categorize the event in the scope of a larger
  operation. E.g. a provider may have two events for the same task, one indicating the beginning and one
  the end, with opcodes of 'start' and 'stop' respectively. Opcodes may also be user-defined.
* An optional 32 bit 'Task' value that maps a specific event to a certain task.
* An optionl 8 bit 'Version' value used for coordinating revisions to the above metadata.

An event has zero or more embedded pieces of data. ETW supports weak structuring using individual arguments
with basic types (8 and 16 bit character strings, integers, floating point values, and byte segments).
Complex/nested structures are not natively supported, in those cases either text (e.g. JSON) or binary
(e.g. Bond) serialization is recommended.

Finally, the ETW contract implies that you will *not* change the metadata above at runtime. That is, once
an event has a particular set of keywords, severity, task, et cetera this won't change for the duration of
that provider's lifetime. This means that events do not dynamically change severity / opcode / et cetera.
This data is all encoded with the manifest and ETW parsers will be broken if runtime changes are made that
break with the declaration of the manifest.

### Sessions

ETW trace sessions come in two broad flavors: realtime and file-backed. In addition sessions may either be
kernel-mode or user-mode.

Realtime sessions use a hidden backing file as a circular buffer and expect somebody attached to the session
to pick up the events as they are emitted. These are broadly useful for work such as sampling, performance
measurement of apps, et cetera.

File-backed sessions use on-disk files to emit binary ETW data which may be processed once the file has been
closed. For user-mode file backed sessions only a single session per process may be open. Additionally, if
the process exits the session necessarily terminates. Kernel-mode sessions stay active until they are
manually terminated or the operating system shuts down.

Within each session one or more providers may be subscribed to. For each provider subscribed to in the
session events from that provider can be filtered based on their severity and keywords. No other metadata
(ID, opcode, et cetera) can be used for filtering. In particular this means you cannot get
"only events 1-10", or "only events for task 57", or "only events where the first string argument is
`abcdefg`."

## Reading ETL Logs

The [LogTool utility](tree/master/utils/LogTool) provides code to build an executable called 'ELT' (formerly
'BLT' when this code was Bing-internal). This tool is both meant to demonstrate the various library
facilities provided by the codebase and to present an easy-to-use command line interface for interacting
with ETW sessions. It can be used to dump the contents of any ETW file complete with parsed arguments (e.g.
kernel, any EventSource provider, and any registered ETW provider). The data can be dumped either as an
easy-to-read text format or in JSON or XML.

You can also use the tool to stand up realtime listening sessions that emit to the console for quick
run-time debugging.

