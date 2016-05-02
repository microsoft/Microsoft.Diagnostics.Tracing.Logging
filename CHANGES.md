# v1.0.0

**NOTE**: Changes here are relative to the previously Microsoft-internal codebase this package
came from.

* BREAKING: Namespace changed from `Microsoft.Bing.Logging` to `Microsoft.Diagnostics.Tracing.Logging` to be
  in step with the `Microsoft.Diagnostics.Tracing` package.
* BREAKING: `AllowEtwLogging` settings to forcefully disable use of ETW logs now controlled through
  `LogManager.Configuration` object (variable on `LogManager` has been removed).
* BREAKING: The min/max/default `FileBufferSizeMB` values on `LogManager` are now
  named `[foo]LogBufferSizeMB` instead to reflect their potential use in non-file logs.
* BREAKING: Several functions were obsoleted with preferable replacements, particularly in terms of using
  the manager to create/find/close loggers and manipulate configuration.
  * Basically creating a logger is now done using the new `LogConfiguration` type which can be used
    to specify all relevant details of a log.
  * These will go away in a future release.
* Configuration is now backed by public types which are the preferred mechanisms for setting
  configuration outside of files.
* Configuration strings may now be (preferably!) provided as JSON. Future configuration options will
  only be supported via JSON configuration.
* Configuring a negative rotation interval is no longer an error and simply indicates "use the default."
* Some public members in public types converted to properties for potential future use.
* `MemoryLogger` can now be directly instantiated without use of the `LogManager`.
* EventSourceSubscriptions can now be created with names of EventSource types. This is
  primarily useful in conjunction with LogConfiguration objects which manage the addition
  of late-bound subscriptions to underlying logs. It is an error to use the `SubscribeToEvents`
  methods on loggers to add unresolved subscriptions (subscriptions can be verified using the
  `IsResolved` member).

