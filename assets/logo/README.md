# Logo

The Groovefin logo at 1024 × 1024 px, drawn by `scripts/assets/make-logo.py` (needs Pillow) from the
measurements of the app's `Square150x150Logo.scale-200.png`:

```sh
python3 scripts/assets/make-logo.py 1024 assets/logo/groovefin-1024.png
python3 scripts/assets/make-logo.py 1024 assets/logo/groovefin-1024-full-bleed.png --full-bleed
```

| File | Use |
| --- | --- |
| `groovefin-1024.png` | The app icon: the rounded tile with transparent corners. README, Store, Discord. |
| `groovefin-1024-full-bleed.png` | The gradient fills the whole square, for places that round the corners themselves. |

## Discord

The Discord status (#160) shows the Rich Presence art asset named `groovefin`
(`DiscordMessages.IconAsset`). In the Discord Developer Portal, open the application, go to
*Rich Presence → Art Assets* and upload `groovefin-1024.png` under that name. New assets can take a
few minutes to show up.
