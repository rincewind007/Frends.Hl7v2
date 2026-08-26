# Changelog

## [1.3.0] - 2026-08-26

### Added

- `Options.CorrectWhitespaces`: controls whether nhapi normalizes whitespace in the XML while parsing (default `true`, matching previous behavior).
- `Options.CrashOnUnknownTags`: throws an error when the XML contains a tag nhapi does not recognize, instead of silently adding it inline (default `false`, matching previous behavior).
- `Options.CrashOnDataLoss`: throws an error when nhapi silently fails to place a value from the input XML into the parsed HL7v2 message, e.g. when a composite/nested field is given as flat text (default `false`, matching previous behavior).

## [1.2.0] - 2026-08-06

### Changed

- Updated copyright information to comply with Frends platform standards.

## [1.1.0] - 2026-07-03

### Added

- MSH field overrides (MshOverrides): ability to overwrite any MSH field (MSH-1 to MSH-15) in the output HL7v2 message
CR option to LineEnding enum

## [1.0.0] - 2026-02-27

### Added

- Initial implementation
