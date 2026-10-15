# Notices

QtSharp source code is released under the MIT License. This repository also builds on projects
and distributions that have their own terms:

- Qt is used under the applicable GNU LGPL or commercial Qt license. Qt is not included in this
  repository; users must obtain a compatible Qt distribution and comply with its license.
- ClangSharp and `libClangSharp.runtime.win-x64` are restored from NuGet and retain their own
  licenses and notices.
- The `QtSharp.Tools` package redistributes selected Qt command-line tools and runtime libraries.
  The package builder accepts a `QtSharp-ThirdPartyNotices` directory beside the source tools so
  distributors can include the notices supplied by their Qt distribution.

When redistributing binaries built from this repository, preserve this notice and include the
licenses required by the Qt kit, NuGet packages, and any other bundled third-party components.
