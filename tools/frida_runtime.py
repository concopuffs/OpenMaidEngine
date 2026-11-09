"""Frida for our tools: the real `frida` module, shut down when the process exits.

Frida extracts its injector (frida-helper-*.exe and a frida-agent.dll per architecture, about 53 MB) into a new
%TEMP%\\frida-<id> folder in every process that injects, and deletes that folder only when its device manager is
closed. The Python binding never closes it on its own, so each run of a tool that imported plain `frida` left one
folder behind. Import it as `from frida_runtime import frida` instead: frida.shutdown() then runs at interpreter
exit, which closes the device manager and removes the folder. A process that is killed still leaves its folder.
"""

import atexit

import frida

atexit.register(frida.shutdown)
