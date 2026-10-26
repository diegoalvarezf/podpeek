#!/usr/bin/env node
// Arranca PodPeek en segundo plano: Windows PowerShell 5.1 oculto, a través de un .vbs para que no quede
// ninguna ventana de consola abierta (igual que claude-usage-tracker).
const { spawn } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');

if (process.platform !== 'win32') {
  console.error('PodPeek solo funciona en Windows.');
  process.exit(1);
}

const script = path.join(__dirname, '..', 'script', 'podpeek.ps1');
const vbs = path.join(os.tmpdir(), 'podpeek-launch.vbs');
fs.writeFileSync(vbs,
  'Set sh = CreateObject("WScript.Shell")\r\n' +
  `sh.Run "powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File """ & "${script.replace(/\\/g, '\\\\')}" & """", 0, False\r\n`);
spawn('wscript.exe', [vbs], { detached: true, stdio: 'ignore' }).unref();
console.log('PodPeek arrancado: búscalo junto al reloj. Abre el estuche de los AirPods cerca del PC.');
