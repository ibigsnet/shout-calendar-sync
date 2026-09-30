# Shout Calendar Sync

Requires [Shout Calendar](https://github.com/ibigsnet/shout-calendar). This plugin does not open its own calendar. Install Shout Calendar first, then Shout Calendar Sync, and open Shout Calendar for the shared invites and the Sync settings.

It shares public Shout and Yell invites. Say, tells, party, alliance, free company, linkshells, and novice network stay on your machine.

This is not on the official plugin list. In game, open `/xlsettings`, go to Experimental, and add this custom repository:

`https://raw.githubusercontent.com/ibigsnet/shout-calendar/main/pluginmaster.json`

Then install both plugins from `/xlplugins`.

The relay is the small service clients use to exchange those invites. From this folder:

```bash
docker build -t shout-calendar-relay .
docker run -d --name shout-calendar-relay -p 8787:8787 -v shout-calendar:/data shout-calendar-relay
```

`deploy/compose.relay.yml` and `deploy/Caddyfile.relay` are the same service behind HTTPS.
