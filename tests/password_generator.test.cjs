const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const { test } = require('node:test');
const vm = require('node:vm');
const { webcrypto } = require('node:crypto');

const source = readFileSync(resolve(__dirname, '../Arkanum.Web/wwwroot/js/site.js'), 'utf8');

function loadGenerator(crypto = webcrypto) {
  const context = vm.createContext({
    crypto,
    document: {
      documentElement: { dataset: {} },
      querySelectorAll: () => [],
      querySelector: () => null,
    },
    localStorage: { getItem: () => null },
    sessionStorage: { getItem: () => null, removeItem: () => {} },
    window: { addEventListener: () => {} },
  });
  // Expose closure-local functions only in the test VM, not in the shipped script.
  vm.runInContext(source.replace(/\}\)\(\);\s*$/, `
    globalThis.generator = {
      generatePassword,
      randomIndex: typeof randomIndex === 'function' ? randomIndex : undefined,
    };
  })();`), context);
  return context.generator;
}

test('bounded random selection rejects the incomplete range instead of using modulo', () => {
  let reads = 0;
  const samples = [0xffffffff, 0];
  const generator = loadGenerator({ getRandomValues(buffer) {
    assert.ok(reads < samples.length, 'unexpected extra randomness request');
    buffer[0] = samples[reads++];
    return buffer;
  } });
  assert.equal(typeof generator.randomIndex, 'function', 'unbiased bounded sampler is required');
  assert.equal(generator.randomIndex(23), 0);
  assert.equal(reads, 2, 'out-of-range sample must be rejected');
});

test('bounded selection represents every index equally in a complete bit-mask range', () => {
  // Cover every shuffle bound plus every character-set/combined-alphabet size.
  for (const bound of [...Array.from({ length: 25 }, (_, i) => i + 1), 79]) {
    const maskRange = 2 ** Math.ceil(Math.log2(bound));
    const counts = Array(bound).fill(0);
    for (let sample = 0; sample < maskRange; sample++) {
      let reads = 0;
      const generator = loadGenerator({ getRandomValues(buffer) {
        buffer[0] = reads++ === 0 ? sample : 0;
        assert.ok(reads <= 2, 'sampler failed to terminate after valid input');
        return buffer;
      } });
      assert.equal(typeof generator.randomIndex, 'function');
      const index = generator.randomIndex(bound);
      assert.ok(index >= 0 && index < bound);
      if (sample < bound) counts[index]++;
      else assert.equal(reads, 2);
    }
    assert.deepEqual(counts, Array(bound).fill(1));
  }
});

test('generated passwords retain length, allowed alphabet, and all required character classes', () => {
  const generator = loadGenerator();
  const classes = ['ABCDEFGHJKLMNPQRSTUVWXYZ', 'abcdefghijkmnopqrstuvwxyz', '23456789', '!@#$%^&*()-_=+[]{}:,.?'];
  const allowed = classes.join('');
  for (let i = 0; i < 200; i++) {
    const password = generator.generatePassword();
    assert.equal(password.length, 24);
    assert.ok([...password].every(char => allowed.includes(char)));
    for (const set of classes) assert.ok([...password].some(char => set.includes(char)));
  }
});

test('generation rejects out-of-range samples for character selection and shuffle', () => {
  let reads = 0;
  const generator = loadGenerator({ getRandomValues(buffer) {
    assert.ok(reads < 200, 'generation must terminate with alternating invalid/valid samples');
    buffer[0] = reads++ % 2 === 0 ? 0xffffffff : 0;
    return buffer;
  } });
  const password = generator.generatePassword();
  assert.equal(password.length, 24);
  assert.ok(reads > 47, 'character selection and shuffle must reject invalid samples');
});
