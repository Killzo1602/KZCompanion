# Changelog

All notable changes to KZ Companion are recorded here. Release entries are written from
[Conventional Commits](https://www.conventionalcommits.org/) by hand for each release; the release notes live in `docs/release-notes/`.

History of the upstream project this was forked from (DroidDeck 1.0 to 1.14.0, by Gabriel Freitas) is kept in
[CHANGELOG-upstream-DroidDeck.md](CHANGELOG-upstream-DroidDeck.md).

## 1.0.0 (first public release; not yet published)

KZ Companion 1.0.0 is the first release under the KZ Companion name. It replaces the DroidDeck button deck
with a dashboard and remote control for a Windows PC.

### Added

- Android Dashboard: live CPU, GPU and RAM, plus a Hardware page. Unsupported sensors (for example CPU temperature) are shown as unavailable.
- Windows audio mixer: master volume and mute, per-app volume and mute, microphone mute and system Deafen.
- Favorites and application launching, with installed application and game discovery (including Steam and Battle.net).
- Running Apps: Focus/Restore, Minimize and graceful Close, with per-app volume on the same row.
- Windows configurator: Applications, Integrations catalog and Appearance. Android Appearance settings.
- QR pairing with an API key. Update checks through GitHub Releases (Velopack).
- Integrations catalog with honest status labels. Setup screens for Discord, Spotify and OBS Studio; none verified against a live account yet.

### Changed

- Rebranded from DroidDeck to KZ Companion (see NOTICE).
- Removed the legacy button deck, profiles, automations and Stream Deck import.

### Known limitations

- Traffic between phone and PC is plain HTTP on the local network. No end-to-end encryption.
- The Windows installer is not code-signed.
- Apple Music support is unverified. Spotify, OBS and Discord have not been exercised against live services in release testing.
