@echo off
rem Rounds every image in the CURRENT folder and overwrites the originals.
rem Usage (from the folder with your images):  round-overwrite.bat [radiusPercent]
"%~dp0PixPolish.exe" round --overwrite %*
