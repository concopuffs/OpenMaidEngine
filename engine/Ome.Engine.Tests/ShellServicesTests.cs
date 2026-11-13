using Ome.Engine.Diagnostics;
using Ome.Engine.Devices;
using Ome.Engine.Hosting;
using Ome.Engine.Model;
using Ome.Engine.Natives;
using Ome.Engine.Persistence;

/// <summary>
/// The shell-services native (Ome.Engine/Natives/ShellServices*.cs; engine layering milestone 9) on its own: the menu
/// requests and the callbacks they launch, the scene reset, the dialogs as requests the front end completes, and the
/// screen mode it decides and the window shows.
/// </summary>
public class ShellServicesTests
{
    private sealed class RecordingWindow : IWindowDevice
    {
        public readonly List<bool> Shown = [];
        public void ShowScreenMode(bool windowed) => Shown.Add(windowed);
    }

    /// <summary>The front end's dialogs: keeps each it was asked to show, answers none.</summary>
    private sealed class RecordingDialogs : IShellDialogs
    {
        public readonly List<ShellDialog> Shown = [];
        public void ShowDiagnostic(DiagnosticBox box) => Shown.Add(box);
        public void ShowScriptError(ScriptErrorBox box) => Shown.Add(box);
        public void ShowTextEntry(TextEntry entry) => Shown.Add(entry);
    }

    private static readonly DiagnosticMessage Box = new("AGE", "body");

    [Fact]
    public void MenuRequestsQueueInOrderAndWakeTheWaits()
    {
        var shell = ShellServices.Headless();
        int wakes = 0;
        shell.WakeWaitsWith(() => wakes++);

        shell.RequestMessageSkipToggle();
        shell.RequestMessageSettings(new MessageSettings(20, 3, 1500, 4000));
        shell.RequestAutoMessageToggle();
        shell.RequestReadMessageSkipToggle();

        Assert.Equal(4, wakes);
        Assert.True(shell.HasMenuRequests);
        Assert.Equal(
            [
                new ShellMenuRequest(ShellMenuRequestKind.MessageSkipToggle),
                new ShellMenuRequest(ShellMenuRequestKind.MessageSettings, new MessageSettings(20, 3, 1500, 4000)),
                new ShellMenuRequest(ShellMenuRequestKind.AutoMessageToggle),
                new ShellMenuRequest(ShellMenuRequestKind.ReadMessageSkipToggle),
            ],
            shell.TakeMenuRequests());
        Assert.False(shell.HasMenuRequests);
        Assert.Empty(shell.TakeMenuRequests());
        // Taking a request does not launch anything: the interpreter applies it, then asks for CALLBACK_SETTING.BIN.
        Assert.Null(shell.TakeCallbackScript());
    }

    [Fact]
    public void CallbackSettingComesBeforeCallbackWindowAndEachRunsOnce()
    {
        var shell = ShellServices.Headless();

        Assert.True(shell.SwitchScreenMode(windowed: false, "the test"));   // queues CALLBACK_WINDOW.BIN
        Assert.Empty(shell.TakeMenuRequests());                            // ... which the take turns into a launch
        shell.RequestSettingCallback();
        shell.RequestSettingCallback();

        Assert.Equal("CALLBACK_SETTING.BIN", shell.TakeCallbackScript());
        Assert.Equal("CALLBACK_WINDOW.BIN", shell.TakeCallbackScript());
        Assert.Null(shell.TakeCallbackScript());
    }

    [Fact]
    public void ASceneResetDropsRequestsAndCallbacksRestoresTheSystemMenuAndCancelsDialogs()
    {
        var shell = ShellServices.Headless();
        shell.SystemMenuActionsEnabled = 0;
        shell.SystemMenuShowDelayMilliseconds = 1000;
        shell.SystemKeyDisableMask = 0x201;
        shell.RequestMessageSkipToggle();
        shell.RequestSettingCallback();
        shell.AppendDiagnostic("kept");
        DiagnosticBox box = shell.OpenDiagnostic(Box);

        shell.ResetScene();

        Assert.Equal(1, shell.SystemMenuActionsEnabled);
        Assert.Equal(0u, shell.SystemMenuShowDelayMilliseconds);
        Assert.Equal(0, shell.SystemKeyDisableMask);
        Assert.False(shell.HasMenuRequests);
        Assert.Null(shell.TakeCallbackScript());
        Assert.True(box.IsCancelled);
        Assert.True(box.Closed.WaitOne(0));
        // Op 0x1b2's text lives as long as EngineCtx: a scene reset keeps it.
        Assert.Equal("kept", shell.PendingDiagnosticText);
    }

    [Fact]
    public void TheDiagnosticTextIsSharedAcrossShellsOverOneState()
    {
        var text = new DiagnosticOutputState();
        var first = ShellServices.Headless(text);
        var second = ShellServices.Headless(text);

        first.AppendDiagnostic("a");
        second.AppendDiagnostic("b");

        Assert.Equal("ab", first.PendingDiagnosticText);
        second.ClearDiagnostic();
        Assert.Equal("", first.PendingDiagnosticText);
    }

    [Fact]
    public void ADialogIsARequestTheFrontEndCompletesOnce()
    {
        var dialogs = new RecordingDialogs();
        var shell = new ShellServices(dialogs: dialogs);

        ScriptErrorBox error = shell.OpenScriptError(Box);
        TextEntry entry = shell.OpenTextEntry(new FullwidthTextEditRequest("魔王", "勇者"));

        Assert.Equal<ShellDialog>([error, entry], dialogs.Shown);
        Assert.True(error.IsOpen);
        Assert.Equal(ScriptErrorChoice.Abort, error.Choice);   // until an answer comes
        Assert.Equal(new FullwidthTextEditResult(false, "魔王"), entry.Result);
        Assert.False(error.Closed.WaitOne(0));

        error.Answer(ScriptErrorChoice.Ignore);
        error.Answer(ScriptErrorChoice.Retry);   // a second answer is dropped
        entry.Answer(accepted: true, "勇者");

        Assert.True(error.Closed.WaitOne(0));
        Assert.Equal(ScriptErrorChoice.Ignore, error.Choice);
        Assert.False(error.IsCancelled);
        Assert.Equal(new FullwidthTextEditResult(true, "勇者"), entry.Result);
    }

    [Fact]
    public void AnAnswerAfterACancelIsDroppedAndCannotAnswerTheNextDialog()
    {
        var shell = ShellServices.Headless();
        ScriptErrorBox first = shell.OpenScriptError(Box);

        shell.CancelDialogs();   // a stop or a return to the title
        first.Answer(ScriptErrorChoice.Retry);
        ScriptErrorBox second = shell.OpenScriptError(Box);

        Assert.True(first.IsCancelled);
        Assert.Equal(ScriptErrorChoice.Abort, first.Choice);
        Assert.True(second.IsOpen);
        Assert.False(second.Closed.WaitOne(0));
    }

    [Fact]
    public void ASwitchStoresTheModeTellsTheWindowAndLogs()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var settings = new Sys4RegIniStore(Path.Combine(root, "SYS4REG.INI"), defaultMusicRouteValue: 2);
            settings.SaveInt("display", "ScreenMode", 1);
            var window = new RecordingWindow();
            var log = new List<string>();
            var shell = new ShellServices(settings, window, print: log.Add);
            shell.StartScreenMode(windowedArgument: false);

            Assert.True(shell.Windowed);
            Assert.False(shell.SwitchScreenMode(windowed: true, "menu ｳｲﾝﾄﾞｳ"));   // already a window
            Assert.True(shell.SwitchScreenMode(windowed: false, "menu ﾌﾙｽｸﾘｰﾝ"));

            Assert.False(shell.Windowed);
            Assert.Equal([false], window.Shown);
            Assert.Equal(0, settings.LoadInt("display", "ScreenMode", -1));
            Assert.Equal(
                [
                    "[display] starting in a window (display:ScreenMode=1, ForceScreen=0)",
                    "[display] menu ﾌﾙｽｸﾘｰﾝ: fullscreen; CALLBACK_WINDOW.BIN queued",
                ],
                log);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ForceScreenKeepsTheWindowAndANewProfileStartsFullscreen()
    {
        string root = NewTemporaryDirectory();
        try
        {
            var settings = new Sys4RegIniStore(Path.Combine(root, "SYS4REG.INI"), defaultMusicRouteValue: 2);
            var window = new RecordingWindow();
            var log = new List<string>();
            var fresh = new ShellServices(settings, window, print: log.Add);
            fresh.StartScreenMode(windowedArgument: false);   // display:ScreenMode 0: fullscreen, shown at once

            Assert.False(fresh.Windowed);
            Assert.Equal([false], window.Shown);
            Assert.Null(fresh.TakeCallbackScript());   // starting is not a switch: nothing stored either
            Assert.False(File.Exists(settings.FilePath));

            settings.SaveInt("display", "ForceScreen", 1);
            var forced = new ShellServices(settings, window, print: log.Add);
            forced.StartScreenMode(windowedArgument: true);   // --windowed

            Assert.True(forced.ForceScreen);
            Assert.False(forced.SwitchScreenMode(windowed: false, "Alt+Enter"));
            Assert.True(forced.Windowed);
            Assert.Equal("[display] Alt+Enter: display:ForceScreen=1 keeps the window", log[^1]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Op0x2cfsRequestWaitsForTheFrontEndsFrame()
    {
        var window = new RecordingWindow();
        var shell = new ShellServices(window: window);

        shell.RequestScreenMode(windowed: false);
        Assert.True(shell.Windowed);
        shell.ServiceFrame();
        shell.ServiceFrame();   // taken once

        Assert.False(shell.Windowed);
        Assert.Equal([false], window.Shown);
    }

    [Fact]
    public void TheHostsModalWaitEndsWithTheAnswerOrAStop()
    {
        var answering = new AnsweringDialogs(ScriptErrorChoice.Retry);
        using var locator = new PageLocatorState("SC0000", null);
        EngineHost host = EngineHostTests.Host(EngineHostTests.NothingAtAll(), locator, new FrameClock(),
                                               shell: new ShellServices(dialogs: answering));

        Assert.Equal(ScriptErrorChoice.Retry, host.ShowScriptError(Box));

        // A dialog nobody answers: a stop cancels it, and the wait ends with 中止.
        using var locator2 = new PageLocatorState("SC0000", null);
        var silent = new RecordingDialogs();
        EngineHost stopped = EngineHostTests.Host(EngineHostTests.NothingAtAll(), locator2, new FrameClock(),
                                                  shell: new ShellServices(dialogs: silent));
        var waiting = Task.Run(() => stopped.ShowScriptError(Box));
        SpinWait.SpinUntil(() => silent.Shown.Count == 1, TimeSpan.FromSeconds(10));
        stopped.Stop();

        Assert.True(waiting.Wait(TimeSpan.FromSeconds(10)));
        Assert.Equal(ScriptErrorChoice.Abort, waiting.Result);
        Assert.True(silent.Shown[0].IsCancelled);
    }

    /// <summary>Answers each script error box at once, as a front end would later on its own thread.</summary>
    private sealed class AnsweringDialogs(ScriptErrorChoice choice) : IShellDialogs
    {
        public void ShowDiagnostic(DiagnosticBox box) => box.Close();
        public void ShowScriptError(ScriptErrorBox box) => Task.Run(() => box.Answer(choice));
        public void ShowTextEntry(TextEntry entry) => entry.Answer(false, entry.Request.CurrentText);
    }

    private static string NewTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "ome-shell-services-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
