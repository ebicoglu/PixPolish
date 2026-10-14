@echo off
rem Lists images in the CURRENT folder not referenced by the markdown file(s), without deleting anything.
rem Usage: cleanup-dry-run.bat [file.md]
"%~dp0PixPolish.exe" cleanup --dry-run %*
