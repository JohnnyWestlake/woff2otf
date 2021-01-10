# woff2otf

This is a small utility to convert WOFF files to the OTF font format. 

This uses C# 8, and should run on .NET Standard 2.0+.

## Usage
```
Stream source = ... // source file stream (WOFF file)
Stream output = ... // output stream (OTF file)
WoffToOtf.Converter.Convert(source, output);
```