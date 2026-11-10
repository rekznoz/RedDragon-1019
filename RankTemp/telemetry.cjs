const FRAME_TYPE = 0x01;
const PAYLOAD_SIZE = 10;
const HIDDEN = 0xffff;

// Only the three display fields have been identified by toggling them.
// Keep the final two words raw until their meaning and units are verified.
function decodeTelemetry(frame) {
  if (!Buffer.isBuffer(frame) || frame.length !== 2 + PAYLOAD_SIZE ||
      frame[0] !== FRAME_TYPE || frame[1] !== PAYLOAD_SIZE) {
    throw new TypeError('Expected a RankTemp 1019 telemetry frame');
  }

  const words = [];
  for (let offset = 2; offset < frame.length; offset += 2) {
    words.push(frame.readUInt16BE(offset));
  }
  const [cpuTemperature, pumpSpeed, cpuFanSpeed, word4, word5] = words;
  return {
    cpuTemperatureTenths: cpuTemperature === HIDDEN ? null : cpuTemperature,
    pumpSpeed: pumpSpeed === HIDDEN ? null : pumpSpeed,
    cpuFanSpeed: cpuFanSpeed === HIDDEN ? null : cpuFanSpeed,
    word4,
    word5
  };
}

function encodeTelemetry({ cpuTemperatureTenths, pumpSpeed, cpuFanSpeed, word4, word5 }) {
  const displayWords = [cpuTemperatureTenths, pumpSpeed, cpuFanSpeed].map(value => {
    if (value === null) return HIDDEN;
    if (!Number.isInteger(value) || value < 0 || value >= HIDDEN) {
      throw new RangeError('Expected a display word from 0 to 65534, or null');
    }
    return value;
  });
  const frame = Buffer.alloc(2 + PAYLOAD_SIZE);
  frame[0] = FRAME_TYPE;
  frame[1] = PAYLOAD_SIZE;
  for (const [index, word] of [...displayWords, word4, word5].entries()) {
    if (!Number.isInteger(word) || word < 0 || word > 0xffff) {
      throw new RangeError('Expected a 16-bit unsigned word');
    }
    frame.writeUInt16BE(word, 2 + index * 2);
  }
  return frame;
}

module.exports = { decodeTelemetry, encodeTelemetry };
