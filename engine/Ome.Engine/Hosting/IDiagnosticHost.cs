using Ome.Engine.Model;

namespace Ome.Engine.Hosting;

public interface IDiagnosticHost
{
    /// <summary>Report a recoverable runtime discrepancy while allowing script execution to continue.</summary>
    void ReportWarning(string message) => System.Console.Error.WriteLine(message);

    /// <summary>Present a modal diagnostic and return only after the user dismisses it.</summary>
    void ShowDiagnosticMessage(DiagnosticMessage message)
        => System.Console.Error.WriteLine($"{message.Caption}: {message.Text}");

    /// <summary>Present an interpreter error with 中止/再試行/無視 and return the answer once the user picks one.
    /// A host without a modal box shows the text and aborts.</summary>
    ScriptErrorChoice ShowScriptError(DiagnosticMessage message)
    {
        ShowDiagnosticMessage(message);
        return ScriptErrorChoice.Abort;
    }
}
