@echo off
rem The browser tools: the Playwright MCP server, set up for this sandbox. See ..\browser-mcp.js.
rem The Windows twin of the shell script beside it, for an agent CLI whose commands run under cmd.exe.
node "%~dp0..\browser-mcp.js" %*
