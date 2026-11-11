# OCR components

The Windows x64 engine is built from [Tesseract 5.5.2](https://github.com/tesseract-ocr/tesseract/releases/tag/5.5.2) and [Leptonica 1.85.0](https://github.com/DanBloomberg/leptonica/tree/1.85.0) using [LLVM-MinGW 20260616 UCRT](https://github.com/mstorsjo/llvm-mingw/releases/tag/20260616). English and Russian trained data come from [tessdata_fast 4.1.0](https://github.com/tesseract-ocr/tessdata_fast/tree/4.1.0).

The executable links Leptonica and the compiler libraries statically. It imports only Windows system libraries. Leptonica uses its built-in BMP decoder. Optional image codecs, curl, libarchive, OpenMP, graphics, and training tools are disabled. The Tesseract build defines TESSERACT_DISABLE_DEBUG_FONTS for this configuration.

Tesseract, its trained data, Leptonica, LLVM, and the MinGW-w64 runtime retain their respective licenses in this directory.
