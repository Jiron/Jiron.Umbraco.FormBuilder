# Changelog

All notable changes to Jiron Form Builder are recorded in this file. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html) within each Umbraco major line (the package major
follows the Umbraco major).

## [Unreleased]

### Added

- The repository foundation: the build settings, the git hooks (commit identity, secret scan, private term
  guard, text check) and the community files (security policy, contribution guide, code of conduct, support).
- The solution and its projects: `Jiron.Umbraco.FormBuilder.Core`, `Jiron.Umbraco.FormBuilder.Web` and the
  `Jiron.Umbraco.FormBuilder` package that sites install, for Umbraco `[17.5.0, 18.0.0)`, plus the test projects.
- The client workspace: the website runtime and web component, served from the Web assembly under `/jfb/assets/`
  with Brotli and gzip copies, and the backoffice extension under `/App_Plugins/JironFormBuilder/`, each built file
  carrying the licence notice; size budgets and a pack check that refuses missing, stale or unmarked assets.
