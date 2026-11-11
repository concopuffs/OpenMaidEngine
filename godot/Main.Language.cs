using System;
using System.IO;
using Godot;
using Ome.Engine.Localization;
using Ome.Engine.Sys4;
using Ome.Mods.Localization;

/// <summary>
/// The game text's language (localization spec §10, milestone 1). ui:Language (or <c>--language</c>) selects it:
/// the source language draws the scripts' text as written; <c>x-pseudo</c> presents every string lengthened and
/// bracketed; any other tag presents the catalogs <c>--text-root DIR</c> holds for it and its parents, and with none
/// draws as written (gate 1). <c>--text-log</c> writes every string that reached the screen without an entry to
/// <c>diagnostics/text-misses.jsonl</c>. Packages carry <c>text/</c> from milestone 4.
/// </summary>
public partial class Main
{
    private const string TextMissLogFile = "text-misses.jsonl";
    private const double TextMissFlushSeconds = 2.0;

    private LanguageLaunch? _languageLaunch;
    private TextMissLog? _textMisses;
    private string? _textMissLogPath;
    private double _textMissFlushElapsed;

    /// <summary><c>--text-root</c> and <c>--text-log</c>, with the install's own scripts (before the mod layer) for
    /// the site entries' fingerprints. False stops startup.</summary>
    private bool PrepareLanguage(string[] userArgs, Sys4AssetCatalog baseCatalog, IAssetStore baseStore)
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
        _languageLaunch = new LanguageLaunch(root, LanguageLaunch.BaseFingerprints(baseCatalog, baseStore),
                                             LanguageLaunch.PatchedScripts(_modLayer), _textMisses);
        return true;
    }

    /// <summary>
    /// Gives the VM the presenter for <paramref name="language"/>. At startup a catalog that does not load stops the
    /// run, with every problem; a switch from the options panel keeps the previous language instead. Text already on
    /// screen stays; the next run uses the new language.
    /// </summary>
    private bool ApplyGameLanguage(string language, bool startup)
    {
        if (_languageLaunch == null || _vm == null || _host == null) return true;
        var report = new TextLoadReport();
        TextPresenter? presenter;
        try
        {
            presenter = _languageLaunch.Select(language, report);
        }
        catch (Exception error) when (error is TextCatalogException or IOException or UnauthorizedAccessException)
        {
            if (error is TextCatalogException catalog)
                foreach (string problem in catalog.Problems) GD.PushError($"[text] {problem}");
            else GD.PushError($"[text] {error.Message}");
            if (!startup) GD.PushWarning($"[text] {language} did not load; the game text keeps its language");
            return !startup;
        }
        foreach (string line in report.Lines()) GD.Print($"[text] {line}");
        // The host switches rasterizer before the VM presents, and keeps the Unicode one for any presented run.
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
