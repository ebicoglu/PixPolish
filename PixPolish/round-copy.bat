@echo off
rem Rounds every image in the CURRENT folder (or the files you list), keeping the originals and saving <name>-rounded.png copies.
rem Usage: round-copy.bat [files...] [radiusPercent]
"%~dp0PixPolish.exe" round --postfix %*
