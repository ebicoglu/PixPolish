@echo off
rem Makes collage.png from the images in the CURRENT folder (or from the photos you list).
rem Usage: collage.bat [photos...] [--layout n|name] [--list] [--spacing 0-25] [--radius %] [--bg #RRGGBB|transparent] [--size WxH] [--out file.png]
"%~dp0PixPolish.exe" collage %*
