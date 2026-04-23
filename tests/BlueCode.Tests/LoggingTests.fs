module BlueCode.Tests.LoggingTests

open System.Collections.Generic
open Expecto
open Serilog
open Serilog.Core
open Serilog.Events

// Note: NO [<Tests>] attribute — this project uses explicit rootTests registration
// in RouterTests.fs (STATE.md Accumulated Decisions, 04-02). [<Tests>] auto-discovery
// is NOT used. Test registration is done in RouterTests.fs rootTests list.

/// Simple in-memory sink collecting emitted events for assertion.
/// Uses object expression to avoid ctor overload resolution issues with Expecto.
let private makeCaptureSink (events: ResizeArray<LogEvent>) : ILogEventSink =
    { new ILogEventSink with
        member _.Emit(e: LogEvent) = events.Add(e) }

let tests =
    testList "Logging.levelSwitch" [

        testCase "Default Information suppresses Log.Debug events" <| fun _ ->
            // Arrange: create a LOCAL switch (NOT Logging.levelSwitch) to avoid
            // polluting sibling tests. Each test owns its own logger + switch.
            let switch = LoggingLevelSwitch(LogEventLevel.Information)
            let captured = ResizeArray<LogEvent>()
            let logger =
                LoggerConfiguration()
                    .MinimumLevel.ControlledBy(switch)
                    .WriteTo.Sink(makeCaptureSink captured)
                    .CreateLogger()

            // Act: emit one Debug + one Information
            logger.Debug("should not appear")
            logger.Information("should appear")
            logger.Dispose()

            // Assert: Debug suppressed; Information passes through
            let debugCount =
                captured
                |> Seq.filter (fun e -> e.Level = LogEventLevel.Debug)
                |> Seq.length
            let infoCount =
                captured
                |> Seq.filter (fun e -> e.Level = LogEventLevel.Information)
                |> Seq.length
            Expect.equal debugCount 0
                "Debug event should be suppressed at Information minimum level"
            Expect.equal infoCount 1
                "Information event should pass through at Information minimum level"

        testCase "Flipping switch to Debug reveals Log.Debug events" <| fun _ ->
            // Arrange: local switch starting at Information
            let switch = LoggingLevelSwitch(LogEventLevel.Information)
            let captured = ResizeArray<LogEvent>()
            let logger =
                LoggerConfiguration()
                    .MinimumLevel.ControlledBy(switch)
                    .WriteTo.Sink(makeCaptureSink captured)
                    .CreateLogger()

            // Act: emit one Debug BEFORE flip (should be dropped),
            // flip to Debug, emit another (should appear).
            logger.Debug("pre-flip — dropped")
            switch.MinimumLevel <- LogEventLevel.Debug
            logger.Debug("post-flip — kept")
            logger.Dispose()

            // Assert: only the post-flip Debug event appears
            let debugMsgs =
                captured
                |> Seq.filter (fun e -> e.Level = LogEventLevel.Debug)
                |> Seq.map (fun e -> e.MessageTemplate.Text)
                |> Seq.toList
            Expect.equal debugMsgs [ "post-flip — kept" ]
                "Only the post-flip Debug event should appear in captured events"

    ]
