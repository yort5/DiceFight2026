// Card paintings (2026-10-27): every card in cardArt.tsx's map has both its
// files in public/dice-kingdom/art, and each file has a credit line.
const { test } = require("node:test");
const assert = require("node:assert");
const fs = require("node:fs");
const path = require("node:path");

const source = fs.readFileSync(path.join(__dirname, "../src/dicekingdom/cardArt.tsx"), "utf8");
const art = path.join(__dirname, "../public/dice-kingdom/art");
const slugs = [...source.matchAll(/^\s+"[^"]+": "([a-z0-9-]+)",/gm)].map((m) => m[1]);

test("the painting map isn't empty", () => {
  assert.ok(slugs.length >= 69, `only ${slugs.length} paintings found`);
});

test("every painting has a full picture and an icon crop", () => {
  for (const slug of slugs) {
    assert.ok(fs.existsSync(path.join(art, `${slug}.webp`)), `${slug}.webp missing`);
    assert.ok(fs.existsSync(path.join(art, `${slug}-icon.webp`)), `${slug}-icon.webp missing`);
  }
});

test("every painting is credited", () => {
  const credits = fs.readFileSync(path.join(art, "CREDITS.txt"), "utf8");
  for (const slug of slugs) assert.match(credits, new RegExp(`^${slug}: `, "m"), `${slug} has no credit line`);
});
