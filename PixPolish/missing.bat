@echo off
rem Reports images referenced in the markdown file(s) of the CURRENT folder that don't exist on disk.
rem Usage: missing.bat [file.md]
"%~dp0PixPolish.exe" missing %*
