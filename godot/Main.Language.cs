using System;
using System.IO;
using Godot;
using Ome.Engine.Frontend.Shell;
using Ome.Engine.Localization;
using Ome.Engine.Model;
using Ome.Engine.Sys4;
using Ome.Mods.Localization;

/// <summary>
/// The game text's language (localization spec §10). ui:Language (or <c>--language</c>) selects it: the source
/// language draws the scripts' text as written; <c>x-pseudo</c> presents every string lengthened and bracketed; any
/// other tag presents the catalogs of its fallback chain, from the enabled packages' <c>text/</c> (through the link
/// cache) and then from <c>--text-root DIR</c>, with the languages the packages' manifests and the root's
/// <c>languages.toml</c> declare; with none it draws as written (gate 1). While the stack defines any language the VM
/// records the instruction that stores each global string (§5.2), and messages of several runs resolve as one (§6.2).
/// <c>--text-log</c> writes every string that reached the screen without an entry to
/// <c>diagnostics/text-misses.jsonl</c>. The same language selects the shell's tables, with the sources'
/// <c>[[engine]]</c> entries in front of the embedded ones (§11).
/// </summary>
public partial class Main
{
    private const string TextMissLogFile = "text-misses.jsonl";
    private const double TextMissFlushSeconds = 2.0;

    private LanguageLaunch? _languageLaunch;
    private TextMissLog? _textMisses;
    private string? _textMissLogPath;
    private double _textMissFlushElapsed;

    /// <summary>The mod layer's text, <c>--text-root</c> and <c>--text-log</c>, with the install's own scripts (before
    /// the mod layer) for the site entries' fingerprints. False stops startup.</summary>
    private bool PrepareLanguage(string[] userArgs, Sys4AssetCatalog baseCatalog, IAssetStore baseStore,
                                 OpcodeTable table)
    {
        string? root = ArgumentValue(userArgs, "--text-root");
        if (root != null)
        {
            root = Path.GetFullPath(root);
            if (!Directory.Exists(root))
            {
                GD.PushError($"[text] --text-root {root}: no such folder");
                return false;
            }
        }
        if (Array.IndexOf(userArgs, "--text-log") >= 0)
        {
            _textMisses = new TextMissLog();
            _textMissLogPath = Path.Combine(_profilePersistence.DiagnosticsDirectory, TextMissLogFile);
            GD.Print($"[text] misses go to {_textMissLogPath}");
        }
        var sources = new System.Collections.Generic.List<TextSource>();
        if (_modLayer?.TextSource is { } packaged) sources.Add(packaged);
        if (root != null) sources.Add(new TextSource(root));
        ScriptMessageIndex? messages = sources.Count == 0
            ? null
            : new ScriptMessageIndex(table, OmeRuntimeMetadata.LoadOpcodeContracts());
        _languageLaunch = new LanguageLaunch(sources,
            TextSiteBinder.For(LanguageLaunch.BaseFingerprints(baseCatalog, baseStore), _modLayer), _textMisses, messages);
        return true;
    }

    /// <summary>
    /// Gives the VM the presenter for <paramref name="language"/>, and the shell its tables. At startup a catalog
    /// that does not load stops the run, with every problem; a switch from the options panel keeps the previous
    /// language for the game text instead, and the shell takes its embedded tables. Text already on screen stays; the
    /// next run uses the new language.
    /// </summary>
    private bool ApplyGameLanguage(string language, bool startup)
    {
        if (_languageLaunch == null || _vm == null || _host == null)
        {
            if (!startup) UseShellStrings(ShellStrings.Load(language));
            return true;
        }
        // Before the VM runs, so the boot tables are recorded whichever language is selected.
        if (startup) _vm.RecordsStringStores = _languageLaunch.DefinesLanguage;
        var report = new TextLoadReport();
        LanguageSelection selection;
        try
        {
            selection = _languageLaunch.Load(language, report);
        }
        catch (Exception error) when (error is TextCatalogException or IOException or UnauthorizedAccessException)
        {
            if (error is TextCatalogException catalog)
                foreach (string problem in catalog.Problems) GD.PushError($"[text] {problem}");
            else GD.PushError($"[text] {error.Message}");
            if (startup) return false;
            GD.PushWarning($"[text] {language} did not load; the game text keeps its language");
            UseShellStrings(ShellStrings.Load(language));
            return true;
        }
        foreach (string line in report.Lines()) GD.Print($"[text] {line}");
        ApplyEngineText(language, selection, startup);
        TextPresenter? presenter = selection.Presenter;
        // The host switches rasterizer before the VM presents, and keeps the Unicode one for any presented run.
        _host.UsePresentedFonts(presenter?.Language);
        if (presenter != null) _host.PresentsText = true;
        _vm.Presenter = presenter;
        if (presenter == null) _host.PresentsText = false;
        GD.Print(presenter == null
            ? $"[text] {language}: the game text is drawn as written"
            : presenter.Language.Chain.Count == 0
                ? $"[text] {language}: every string is presented lengthened and bracketed"
                : $"[text] {language}: the game text is presented from {string.Join(", ",
                    System.Linq.Enumerable.Select(presenter.Language.Chain, catalog => catalog.Language))}");
        return true;
    }

    /// <summary>The shell's tables for <paramref name="language"/> with the selection's engine text (spec §11). At
    /// startup the bar was built from the embedded tables, so only engine text relabels it.</summary>
    private void ApplyEngineText(string language, LanguageSelection selection, bool startup)
    {
        if (startup && selection.Engine.Count == 0) return;
        var problems = new System.Collections.Generic.List<string>();
        ShellStrings strings = ShellStrings.Load(language, selection.Lookup, selection.Engine, problems);
        foreach (string problem in problems) GD.PushWarning($"[text] {problem}; the engine's own text is shown");
        UseShellStrings(strings);
        if (selection.Engine.Count > 0)
            GD.Print($"[text] {language}: the shell's text comes from {string.Join(", ",
                System.Linq.Enumerable.Select(selection.Engine, layer => layer.Language))} before its own tables");
    }

    /// <summary>Writes the miss log when it changed, every <see cref="TextMissFlushSeconds"/> and at shutdown.</summary>
    private void FlushTextMisses(double delta = double.PositiveInfinity)
    {
        if (_textMisses == null || _textMissLogPath == null) return;
        _textMissFlushElapsed += delta;
        if (_textMissFlushElapsed < TextMissFlushSeconds) return;
        _textMissFlushElapsed = 0;
        try
        {
            _textMisses.WriteIfChanged(_textMissLogPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"[text] writing {_textMissLogPath} failed: {error.Message}");
        }
    }
}
