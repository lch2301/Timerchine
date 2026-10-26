"""Verify the retained Android FFmpeg executables against their source/build lock."""
from pathlib import Path
import argparse
import hashlib
import json
import struct

PROJECT = Path(__file__).resolve().parents[1]
LOCK = PROJECT / 'native/native-artifacts.lock.json'
MACHINES = {'arm64-v8a': 183, 'armeabi-v7a': 40, 'x86_64': 62, 'x86': 3}

def digest(file):
    with file.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest() if hasattr(hashlib, 'file_digest') else hashlib.sha256(stream.read()).hexdigest()

def sources():
    for directory in ('native/ffmpeg', 'native/deps'):
        for file in sorted((PROJECT / directory).rglob('*')):
            relative = file.relative_to(PROJECT)
            if not file.is_file() or any(part in {'build', 'work', '__pycache__'} for part in relative.parts):
                continue
            if file.suffix in {'.py', '.sh', '.cmake', '.xz', '.gz'} or file.name in {'sources.lock.json', 'CMakeLists.txt'}:
                yield file

def inspect_elf(file, abi):
    data = file.read_bytes()
    if data[:4] != b'\x7fELF' or data[5] != 1:
        raise ValueError(f'Not a little-endian Android ELF: {file}')
    kind, machine = struct.unpack_from('<HH', data, 16)
    if machine != MACHINES[abi] or kind != 3:
        raise ValueError(f'Incorrect ABI or non-PIE executable: {file}')
    if data[4] == 2:
        offset = struct.unpack_from('<Q', data, 32)[0]
        size, count = struct.unpack_from('<HH', data, 54)
        alignments = [struct.unpack_from('<Q', data, offset + index * size + 48)[0]
                      for index in range(count) if struct.unpack_from('<I', data, offset + index * size)[0] == 1]
        if not alignments or min(alignments) < 16384:
            raise ValueError(f'64-bit executable must support 16 KB pages: {file}')

def update():
    source_hashes = {file.relative_to(PROJECT).as_posix(): digest(file) for file in sources()}
    artifacts = {}
    for abi in MACHINES:
        for name in ('libffmpeg.so', 'libffprobe.so'):
            file = PROJECT / 'app/src/main/jniLibs' / abi / name
            inspect_elf(file, abi)
            artifacts[file.relative_to(PROJECT).as_posix()] = digest(file)
    LOCK.write_text(json.dumps({'schema': 1, 'ffmpegRevision': '9a9b70bb8c',
                               'ndk': '28.2.13676358', 'sources': source_hashes,
                               'artifacts': artifacts}, indent=2) + '\n', encoding='utf-8')

def verify():
    lock = json.loads(LOCK.read_text(encoding='utf-8'))
    if lock['schema'] != 1 or len(lock['artifacts']) != 8:
        raise ValueError('Unexpected native lock schema or artifact count')
    for relative, expected in {**lock['sources'], **lock['artifacts']}.items():
        file = PROJECT / relative
        if not file.is_file() or digest(file) != expected:
            raise ValueError(f'Native source or binary checksum mismatch: {relative}')
    for relative in lock['artifacts']:
        inspect_elf(PROJECT / relative, Path(relative).parts[-2])
    print('Verified 8 native executables, 4 ABIs, 16 KB support and retained source/build hashes.')

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--update', action='store_true', help='Update only after rebuilding all four ABIs from the retained sources')
    arguments = parser.parse_args()
    if arguments.update:
        update()
    verify()
