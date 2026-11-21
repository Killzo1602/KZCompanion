# Contributing to KZ Companion

Thanks for helping! KZ Companion is a fork of [DroidDeck](https://github.com/ggfto/DroidDeck) and has two
halves in this repository: the Windows app (`DroidDeck/`, C# / .NET 8) and the Android app plus web
configurator (`app/`, Flutter).

**v1.0.0 is in feature freeze.** Until it ships, bug fixes, documentation, translations and tests are
welcome. New features and new integrations should be discussed in an issue first.

## Before you start

- Search [existing issues](https://github.com/Killzo1602/KZCompanion/issues) first.
- For a bug, use the **Bug report** template. Never post credentials, pairing keys, QR codes or tokens.
- Security problems go through [SECURITY.md](SECURITY.md), not public issues.

## Build and test

```powershell
dotnet test DroidDeck.sln -c Release --filter "Category!=RequiresDesktop"   # backend
dotnet run --project DroidDeck                                              # tray app + server, http://localhost:4787

cd app
flutter pub get
flutter analyze --no-fatal-infos
flutter test
flutter build apk --release
```

To produce a Windows build that includes a fresh web configurator, use `scripts\publish-windows.ps1`
(see the README's Development section). Do not copy an old `app\build\web` into `DroidDeck\wwwroot` by hand.

## Pull requests

- Commits follow [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `docs:`...).
  They keep the history readable and feed the changelog and release notes.
- Keep `dotnet test`, `flutter analyze` and `flutter test` green. Say so in the PR if a test cannot run on your machine.
- User-facing text goes through `tr("...")` in the app and `L.T("...")` / `L.Ui("...")` on the PC, so it can be translated.
- Actions that run code or reach the network from the PC must be listed as privileged in `ActionPolicy`.
- Do not claim an integration works unless you ran it against the real service.

## Translations

Each language is one file in [`app/assets/i18n/`](app/assets/i18n/). Copy `en.json` to `<code>.json`, set
`@language`, `@locale` and `@decimal`, and translate the values. Keep the keys and `{placeholders}`.
A partial file is fine; missing strings fall back to English.

## License

By contributing you agree your work is licensed under the [Apache License 2.0](LICENSE). Keep upstream
copyright headers and the [NOTICE](NOTICE) intact.
