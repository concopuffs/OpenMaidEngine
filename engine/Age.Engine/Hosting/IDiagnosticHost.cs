namespace Age.Engine.Hosting;

public interface IDiagnosticHost
{
    /// <summary>Report a recoverable runtime discrepancy while allowing script execution to continue.</summary>
    void ReportWarning(string message) => System.Console.Error.WriteLine(message);

    /// <summary>Present a modal diagnostic and return only after the user dismisses it.</summary>
    void ShowDiagnosticMessage(DiagnosticMessage message)
        => System.Console.Error.WriteLine($"{message.Caption}: {message.Text}");
}
