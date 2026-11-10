const fs = require('node:fs');
const path = require('node:path');

const archivePath = process.argv[2] ??
  path.join(__dirname, '..', 'CpuTemp', 'resources', 'app.asar');
const archive = fs.readFileSync(archivePath);
const headerEnd = 16 + archive.readUInt32LE(12);
const payloadStart = 8 + archive.readUInt32LE(4);
if (headerEnd > payloadStart || payloadStart > archive.length) {
  throw new Error('Cabecera ASAR fuera de los límites del archivo');
}
const header = JSON.parse(archive.toString('utf8', 16, headerEnd));

function entryAt(name) {
  return name.split('/').reduce((directory, part) => directory.files[part], header);
}

function readEntry(name) {
  const entry = entryAt(name);
  const start = payloadStart + Number(entry.offset);
  const end = start + entry.size;
  if (entry.unpacked || !Number.isSafeInteger(end) || start < payloadStart || end > archive.length) {
    throw new Error(`Entrada ASAR no disponible: ${name}`);
  }
  return archive.subarray(start, end);
}

function showMatches(name, source, pattern) {
  console.log(`\n${name}`);
  for (const match of source.matchAll(pattern)) {
    console.log(`  0x${match.index.toString(16).padStart(6, '0')}  ${match[0]}`);
  }
}

for (const component of ['main', 'preload']) {
  const name = `out/${component}/index.jsc`;
  const bytecode = readEntry(name).toString('latin1');
  showMatches(name, bytecode, /\b(?:DATA_FRAME_[A-Z_]+|FRAME_FLAGS_SECTION_SIZE|LENGTH_WITHOUT_DATA_SECTION_SIZE|APP_PROTOCOL_[A-Z_]+|DataFrame|MCUProtocol|getCheckSum|checkSumBuffer|dataSectionLength|dataTotalLength|foundEscapeChar|packData|(?:read|write)U(?:I|i)nt(?:8|16BE)|(?:device|xunling):\w+|service:sync\w+)\b/g);
}

const renderer = 'out/renderer/assets';
for (const name of Object.keys(entryAt(renderer).files).filter(name => name.endsWith('.js'))) {
  const source = readEntry(`${renderer}/${name}`).toString('latin1');
  const evidence = /baudRate\s*:\s*\d+|window\.(?:device\.conn|xunling\.findDevice|service\.sync\w+)/g;
  if (source.search(evidence) >= 0) showMatches(name, source, evidence);
}
