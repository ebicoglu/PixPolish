@echo off
rem Rounds every image in the CURRENT folder (or the files you list) and REPLACES the originals.
rem Usage: round.bat [files...] [radiusPercent]
"%~dp0PixPolish.exe" round %*
