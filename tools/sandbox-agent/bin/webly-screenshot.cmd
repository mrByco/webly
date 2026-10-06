@echo off
rem Full-page screenshots of the site being edited, at desktop and phone width. See ..\screenshot.js.
rem The Windows twin of the shell script beside it, for an agent CLI whose commands run under cmd.exe.
node "%~dp0..\screenshot.js" %*
