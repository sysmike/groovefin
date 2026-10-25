// Renders the app's logo files from assets/logo/groovefin.svg (#259): node scripts/assets/render-logo.cjs
// Needs Playwright with Chromium (npm i -D playwright && npx playwright install chromium). Each file and
// each frame of the .ico is drawn from the vector at its own size, so small icons stay sharp.
const { chromium } = require('playwright');
const { readFileSync, writeFileSync } = require('node:fs');
const { join } = require('node:path');

const root = join(__dirname, '..', '..');
const svg = readFileSync(join(root, 'assets', 'logo', 'groovefin.svg'), 'utf8');
const appAssets = join(root, 'src', 'Groovefin.App', 'Assets');

// fill: the share of the canvas's shorter side the logo's bounding square takes. Small icons use nearly
// all of it; the tiles and the splash screen keep the old logo's size (Windows draws them on a plate).
const icon = 0.96;
const files = [
  { path: join(root, 'assets', 'logo', 'groovefin-1024.png'), width: 1024, height: 1024, fill: 0.93 },
  { path: join(appAssets, 'Square44x44Logo.scale-200.png'), width: 88, height: 88, fill: icon },
  { path: join(appAssets, 'Square44x44Logo.targetsize-24_altform-unplated.png'), width: 24, height: 24, fill: icon },
  { path: join(appAssets, 'Square44x44Logo.targetsize-48_altform-unplated.png'), width: 48, height: 48, fill: icon },
  { path: join(appAssets, 'Square44x44Logo.targetsize-48_altform-lightunplated.png'), width: 48, height: 48, fill: icon },
  { path: join(appAssets, 'LockScreenLogo.scale-200.png'), width: 48, height: 48, fill: icon },
  { path: join(appAssets, 'StoreLogo.png'), width: 50, height: 50, fill: icon },
  { path: join(appAssets, 'Square150x150Logo.scale-200.png'), width: 300, height: 300, fill: 0.8 },
  { path: join(appAssets, 'Wide310x150Logo.scale-200.png'), width: 620, height: 300, fill: 0.68 },
  { path: join(appAssets, 'SplashScreen.scale-200.png'), width: 1240, height: 600, fill: 0.555 },
];
const iconSizes = [16, 24, 32, 48, 64, 128, 256];

(async () => {
const browser = await chromium.launch();
try {
  const page = await browser.newPage();
  // Draws the SVG on a canvas, with the logo's bounding square (found from the pixels) centred.
  const render = (width, height, fill) => page.evaluate(async ({ svg, width, height, fill }) => {
    const image = new Image();
    image.src = 'data:image/svg+xml;base64,' + btoa(unescape(encodeURIComponent(svg)));
    await image.decode();
    const size = image.naturalWidth;
    const probe = new OffscreenCanvas(size, size);
    const pc = probe.getContext('2d');
    pc.drawImage(image, 0, 0, size, size);
    const alpha = pc.getImageData(0, 0, size, size).data;
    let left = size, top = size, right = 0, bottom = 0;
    for (let y = 0; y < size; y++) {
      for (let x = 0; x < size; x++) {
        if (alpha[(y * size + x) * 4 + 3] > 8) {
          left = Math.min(left, x); right = Math.max(right, x);
          top = Math.min(top, y); bottom = Math.max(bottom, y);
        }
      }
    }
    const box = Math.max(right - left + 1, bottom - top + 1);
    const cx = (left + right + 1) / 2, cy = (top + bottom + 1) / 2;
    const scale = (Math.min(width, height) * fill) / box;
    const canvas = new OffscreenCanvas(width, height);
    const c = canvas.getContext('2d');
    c.imageSmoothingQuality = 'high';
    c.drawImage(image, width / 2 - cx * scale, height / 2 - cy * scale, size * scale, size * scale);
    const blob = await canvas.convertToBlob({ type: 'image/png' });
    return [...new Uint8Array(await blob.arrayBuffer())];
  }, { svg, width, height, fill }).then(bytes => Buffer.from(bytes));

  for (const f of files) {
    writeFileSync(f.path, await render(f.width, f.height, f.fill));
    console.log(`${f.path}: ${f.width}x${f.height}`);
  }

  // AppIcon.ico: one PNG frame per size (Windows Vista and later read PNG frames).
  const frames = [];
  for (const s of iconSizes) frames.push(await render(s, s, icon));
  const header = Buffer.alloc(6 + 16 * frames.length);
  header.writeUInt16LE(0, 0);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(frames.length, 4);
  let offset = header.length;
  frames.forEach((png, i) => {
    const s = iconSizes[i], e = 6 + 16 * i;
    header.writeUInt8(s >= 256 ? 0 : s, e);
    header.writeUInt8(s >= 256 ? 0 : s, e + 1);
    header.writeUInt8(0, e + 2);
    header.writeUInt8(0, e + 3);
    header.writeUInt16LE(1, e + 4);
    header.writeUInt16LE(32, e + 6);
    header.writeUInt32LE(png.length, e + 8);
    header.writeUInt32LE(offset, e + 12);
    offset += png.length;
  });
  const ico = join(appAssets, 'AppIcon.ico');
  writeFileSync(ico, Buffer.concat([header, ...frames]));
  console.log(`${ico}: ${iconSizes.join(', ')}`);
} finally {
  await browser.close();
}
})();
