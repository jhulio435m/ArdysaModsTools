// SPDX-License-Identifier: GPL-3.0-or-later
// Live upstream ZIP generation; does not install or modify the game.
import { chromium } from '@playwright/test';
import fs from 'node:fs/promises';
const [sessionFile, output, screenshots] = process.argv.slice(2);
if (!sessionFile || !output || !screenshots) throw new Error('Usage: node tests/catalog-smoke.mjs SESSION_FILE NEW_VPK SCREENSHOT_DIRECTORY');
await fs.mkdir(screenshots, {recursive:true});
const browser=await chromium.launch({executablePath:'/usr/bin/chromium',headless:true,args:['--no-sandbox']});
try {
const page=await browser.newPage();
const errors=[];page.on('pageerror',e=>errors.push(e.message));
await page.goto((await fs.readFile(sessionFile,'utf8')).trim());
await page.getByRole('button',{name:'Generar selección',exact:true}).click();
const catalogResponse=page.waitForResponse(r=>r.url().endsWith('/api/catalog'),{timeout:120000});
await page.getByRole('button',{name:'Cargar catálogo',exact:true}).click();
const cr=await catalogResponse;if(!cr.ok()) throw new Error(await cr.text());
await page.locator('#catalog-hero option').nth(1).waitFor({state:"attached"});
await page.locator('#catalog-hero').selectOption('npc_dota_hero_abaddon');
await page.locator('#catalog-set').selectOption('Assets/models/Abaddon/blightfall.zip');
await page.locator('#catalog-output').fill(output);
const responsePromise=page.waitForResponse(r=>r.url().endsWith('/api/catalog/generate'),{timeout:180000});
await page.getByRole('button',{name:'Descargar y generar set',exact:true}).click();
const response=await responsePromise; console.log('generation',response.status(),await response.text());
if(!response.ok()) throw new Error('Generation failed');
await page.getByText('Set descargado y VPK generado. Ve a Mods para instalarlo.',{exact:true}).waitFor();
await page.screenshot({path:screenshots + "/catalog-desktop.png",fullPage:true});
await page.setViewportSize({width:390,height:844});
if(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth)) throw new Error('Mobile overflow');
await page.screenshot({path:screenshots + "/catalog-mobile.png",fullPage:true});
console.log('hero_count',await page.locator('#catalog-hero option').count(),'page_errors',errors.length);
if(errors.length) throw new Error(errors.join('\n'));
}finally{await browser.close()}
