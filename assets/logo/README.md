# Logo

`groovefin.svg` is the Groovefin logo (#259): a record with a fin. Every logo file of the app is rendered
from it by `scripts/assets/render-logo.cjs`, each at its own size, so small icons stay sharp:

```sh
npm i -D playwright && npx playwright install chromium   # once
node scripts/assets/render-logo.cjs
```

| File | Use |
| --- | --- |
| `groovefin.svg` | The source. Change the logo here, then render again. |
| `groovefin-1024.png` | 1024 × 1024 px on a transparent background: README, Store, Discord. |
| `src/Groovefin.App/Assets/*Logo*.png`, `SplashScreen.scale-200.png` | The app package: Start, taskbar, tiles, Store, lock screen, splash screen. |
| `src/Groovefin.App/Assets/AppIcon.ico` | The executable, the window and the notification area icon: 16 to 256 px. |

The small icons fill almost the whole square. The tiles and the splash screen keep a margin around the logo,
because Windows draws them on a plate.

## Discord

The Discord status (#160) shows the Rich Presence art asset named `groovefin`
(`DiscordMessages.IconAsset`). In the Discord Developer Portal, open the application, go to
*Rich Presence → Art Assets* and upload `groovefin-1024.png` under that name. New assets can take a
few minutes to show up.
