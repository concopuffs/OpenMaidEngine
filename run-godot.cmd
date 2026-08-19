@echo off
REM Default windowed launch follows SYSTEM4, exposes TITLE's shipped developer menu,
REM and uses the accepted GPU retained renderer. Performance logging is opt-in again.
REM -StartupDiagnostics overrides the developer-menu flag for a native-faithful capture.
REM Pass -Kamidori to select the conventional Kamidori install and profile.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-godot.ps1" -NativeDebugMenu %*
