# Third-party notices

The self-contained Windows distribution includes third-party components, which
are not covered by Dockerino Lamborgino's Blue Oak license.

## EasyWindowsTerminalControl 1.0.38

The NuGet package metadata declares the MIT license and the following notice:

Copyright © MitchCapper 2026

Source: https://www.nuget.org/packages/EasyWindowsTerminalControl/1.0.38

## Microsoft.Windows.Console.ConPTY 1.24.260710001

The NuGet package metadata declares the MIT license and the following notice:

© Microsoft Corporation

Source: https://www.nuget.org/packages/Microsoft.Windows.Console.ConPTY/1.24.260710001

## CI.Microsoft.Terminal.Wpf 1.25.260303002

Redistribution of this repackaged WPF control requires
compliance with the original Windows Terminal project's MIT license. The
package metadata identifies commit `9ae724aa5b080aafbeea2bbf88db630b182cc802`,
which exists in the Windows Terminal repository:
https://github.com/microsoft/terminal/commit/9ae724aa5b080aafbeea2bbf88db630b182cc802

Copyright (c) Microsoft Corporation. All rights reserved.

Source: https://github.com/microsoft/terminal/blob/9ae724aa5b080aafbeea2bbf88db630b182cc802/LICENSE

## .NET runtime and Windows Desktop runtime

The .NET runtime and Windows Desktop runtime packages include the following
license. The installer build is configured to include the license files from
the exact restored runtime packs and the .NET runtime pack's third-party
notices under `licenses`. Verify the generated installer contains these files.

Copyright (c) .NET Foundation and Contributors

## MIT license text

The MIT License (MIT)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## Outstanding release review

For `EasyWindowsTerminalControl`, the package metadata explicitly declares
MIT and its copyright notice is reproduced above. The `LICENSE` file at its
recorded repository commit is an HTML page rather than a license text; seeking
maintainer clarification remains prudent.

Verify the notice bundle in the generated installer, including whether
additional notices apply to the Windows Desktop runtime and native terminal
binaries. Recheck the full dependency graph whenever dependencies or runtime
packs change.
