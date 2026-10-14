@echo off
rem Deletes (to Recycle Bin) images in the CURRENT folder not referenced by the markdown file(s).
rem Usage (from the folder with your images):  cleanup.bat [file.md] [--dry-run]
"%~dp0PixPolish.exe" cleanup %*
