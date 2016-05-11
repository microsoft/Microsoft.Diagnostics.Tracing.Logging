# Logging Configuration

Each EventSource provider (and every ETW provider) comes with an associated GUID which is known as its
provider ID. This is the recommended way to dictate which providers/sources appear in your log files. The
logging library framework provides both APIs and a simple JSON-based configuration syntax for arranging the
logging of data. The APIs are described with in-assembly documentation. The JSON objects are described below
and are the recommended way to configure logging for complex applications.

## Configurating individual logs

When providing configuration to the logger you will be expected to provider JSON document with one or
more `LogConfiguration` objects attached to an array with the name `logs` (see below for examples).
Each `LogConfiguration` object has the following properties:

* name (required, except for console): the base name of this log. This is used as the prefix for the log
  files themselves in the case of files.
* type (required): the type of log file to write. Supported types are:
  * `text`: a text file log
  * `etw`: a Windows ETW log.
  * `console`: a singleton logger which emits messages to the console.
  * `network`: a logger which emits XML-serialized messages to a remote endpoint [EXPERIMENTAL]
* sources (required): an array of `EventProviderSubscription` objects which provide sources for the log. At
  least one source is required to be in the array.
* bufferSizeMB (optional): The desired minimum buffer size for the log. For text files there is only one
  buffer. For ETL files there are at minimum two buffers (see
  [this page](http://msdn.microsoft.com/en-us/library/aa363784%28VS.85%29.aspx) -- specifically the
  MinimumBuffers field -- for details). The default value is 2 MB, and depending on the value of the total
  desired buffer size different sizes of individual ETW buffers will be chosen. For high volume logs a
  minimum of 32MB of buffer space is recommended, if not more.
* filenameTemplate (optional): A template for formatting the filename for files with rotation enabled. The
  default template is `{0}_{1:YYYYMMdd}T{1:HHmmss}Z--{2:HHmmss}Z` for UTC stamps, and
  `{0}_{1:YYYYMMdd}T{1:HHmmsszz}--{2:HHmmsszz}` for local stamps.
  Some additional items are also available. Formatting options are:
  * 0: The base of the filename. This is the only mandatory part of the template. This is the value of the
       `name` attribute.
  * 1: The starting time of the rotation interval for the log file. This is when the file was opened.
  * 2: The ending time of the rotation interval for the log file. This is NOT necessarily when the file was
       closed (but typically is.)
  * 3: The name of the machine the logging is occuring on. Not recommended, provided for a specific partner.
       May be deprecated. Don't use this.
  * 4: The number of milliseconds since UTC midnight. Not recommended, provided for a specific partner. May be
       deprecated. Don't use this.
* directory (optional): the directory in which to store log files. If the path is relative it will be
  relative to the default log directory decided upon by the log management API. This path is built by the
  assembly by appending `logs\local` to the $DATADIR environment variable if it exists. If the environment
  variable does not exist the default log directory is the current working directory of the process at the
  time the manager is initialized.
* rotationInterval (optional): the frequency (in seconds) to rotate log files. If this is not specified the
  manager may or may not rotate files, depending on how it has been configured at runtime.
* timestampLocal (optional): Whether the timestamp used in a file configured to rotate should use local
  time. Defaults to false (filename timestamps use UTC). If you use timestampLocal and supply your own
  filename template you should be sure to include the timezone information in that template, or you may lose
  data during daylight savings changes. If you don't specify a custom template this is handled for you.
* maximumAge (optional): string representing a TimeSpan value indicating the maximum age of the oldest log relative to
  the newest. File creation time is used in the calculation.
* maximumSizeMB (optional): Maximum size of all logs in MB before the oldest log(s) will be deleted. Age is
  determined by file creation time.
* filters (optional): an array of regular expression strings which are used to filter log output. Only valid
  for `text` and `console` type logs.

### Retention configuration
Both age and size policies may be applied to the same log. In this case the most aggressive policy
will win (e.g. if a 28 day retention policy is used but only 20GB of logs are allowed then logs
newer than 28 days but which cause the total size to exceed 20GB will be deleted).

When using retention the following additional restrictions are applied for valid configuration:
* Logs must be configured for automatic rotation.
* The base name of the log must be the first portion of the log file name.
* Log templates must not use either the hostname or "milliseconds since midnight" format specifiers
  in their template.
* Log templates must produce a consistent name length for various times of day / days / etc. In
  general it is strongly recommended that the default templates be applied but this is not required.

It is theoretically possible even with the above restrictions to create a situation where retention
mistakenly deletes other logs, though in practice this is very difficult unless the user tries very
hard to fool the system. We would recommend you don't, if you like your logs anyway. In general use
cases this should be very safe to use.

Retention adds some additional process cost as a catalogue of all matching files is kept in memory
and used to quickly determine expiration and perform deletion. The rough cost is the size of the
filename along with an 8-byte value storing size and another 8-byte value storing creation time.
For logs with a (fairly typical) length of approximately 40 characters this means 56 bytes per
entry. The following would be approximate typical per-day sizes at different rotations:
* One minute: 80KB
* Five minute: 16KB
* Ten minute: 8KB
* One hour: 1KB

Retention will never delete the "second to last" file (that is, the last file not currently in use)
even if it would violate retention policies. Effectively, the last completed file will always be
available until the current file is done being processed.

## Configuring data sources for each individual log

Within each `LogConfiguration` object one or more `EventProviderSubscription` objects should exist. Each `EventProviderSubscrption` object
supports the following properties:

* name (required if providerID is not set): The name of the EventSource class which will emit events.
  Note that this is the less preferable way to specify which EventSource you want to use. In particular this
  is a late-binding lookup, until the type is instantiated within your application no subscription will
  occur.
* providerID (required if name is not set): The GUID of the EventSource (or other ETW provider) to write
  events from
* minimumSeverity (optional): The minimum severity of an event before it will be logged. Severity levels
  from least to most severe are `Verbose`, `Informational`, `Warning`, `Error`, `Critical`, and `LogAlways`.
  If the setting is not provided `Informational` is the default.
* keywords (optional): A 64 bit hexadecimal integer value containing the keyword bits you want to filter
  events for. Should not be needed for most simple logs. The default value is `0` and the number may
  optionally be prefixed with `0x` (i.e. if you want `0xbeef` just can also just say `beef`).

## Configuring filtering for text-based logs

Both `text` and `console` type logs can be filtered via regular expression, in addition to filtering based
on keyword/severity at the individual provider level. This can be accomplished by using the `filters` property
to wrap one or more regular expression filters. A single log can have any number of filters and, if a formatted message
matches any one, it will be emitted. See below for an example.

## Configuring of Console Logging

The console logger can be configured similarly to other logs. It must not be given a name, and has a log
type of `console`. It does not support rotation intervals or other attributes of the log tag that make
sense only in the context of file-backed logging.

## Configuration of logging behavior when a process runs without sufficient privileges to establish ETW sessions

Currently Windows requires process elevation to establish kernel-mode ETW logging sessions. By default the
library will determine whether sessions can be safely established and, if they cannot, will downgrade from
ETW to text based logs. This behavior can be changed using the `etwlogging` property at the root of the JSON
configuration. E.g. `etwLogging:true` will force the logger to attempt to establish ETW sessions even if it
does not appear to be safe to do so. Conversely setting this to 'false' will convert all `etl` type sessions
to `text`.

## Sample Configuration

    {"logs":[
      // Create an ETL log which rotates every ten minutes
      {"name":"myLog", "type":"etw", "rotationInterval":600, 
       "sources":[
                  // Consume events from the Microsoft.Diagnostics.Tracing.Logging event source at warning or higher severity
                  {"name":"Microsoft-Diagnostics-Tracing-Logging", "minimumSeverity":"warning"},
                  // Consume events from a custom provider at Informational or higher severity
                  {"providerID":"{SOME GUID HERE}"}]
      },
      // Create a text log which does not rotate
      {"name":"debugStream", "type":"text",
       // Consume all events from a custom provider
       "sources":[{"providerID":"{SOME GUID HERE}", "minimumSeverity":"verbose"}]
      },
      // Modify the console log to look for bacon
      {"type":"console",
       "sources":[{"name":"pig"}],
       "filters":["bacon"]
      }]
    }
