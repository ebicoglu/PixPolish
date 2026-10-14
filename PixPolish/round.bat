@echo off
rem Rounds every image in the CURRENT folder, saving <name>-rounded.png copies.
rem Usage: round.bat [radiusPercent]
"%~dp0PixPolish.exe" round %*
