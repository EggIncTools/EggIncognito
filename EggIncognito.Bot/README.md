# EggIncognito.Bot

The optional Discord bot (Discord.Net, backed by EggIdentity.Bot). Part of [EggIncognito](../README.md).

Opt-in via `Discord:BotToken`; with no token it never starts, and a bot failure never crashes the host. Gateway lifecycle, command registration, and the builtin commands live in EggIdentity.Bot; this project supplies the domain slash commands and their embeds.

Message and embed configuration is edited from the app's own admin page, which needs both Postgres and a bot token, and rides the app's admin login rather than a separate Discord OAuth flow.
