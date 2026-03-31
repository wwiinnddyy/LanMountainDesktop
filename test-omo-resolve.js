
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);

import { getPlatformPackageCandidates, getBinaryPath } from './node_modules/oh-my-opencode/bin/platform.js';

const { platform, arch } = process;
const libcFamily = undefined; // Windows doesn't need libc
const avx2Supported = null; // On Windows, supportsAvx2() returns null

const packageCandidates = getPlatformPackageCandidates({
  platform, arch, libcFamily, preferBaseline: avx2Supported === false
});

console.log('Package candidates:', packageCandidates);

const resolvedBinaries = packageCandidates.map((pkg) => {
  try {
    const binPath = require.resolve(getBinaryPath(pkg, platform));
    return { pkg, binPath };
  } catch (e) {
    console.error('Failed to resolve', pkg, e.message);
    return null;
  }
}).filter((x) => x !== null);

console.log('Resolved binaries:', resolvedBinaries);
