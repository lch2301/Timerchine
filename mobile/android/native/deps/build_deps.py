"""Build the desktop-pinned optional FFmpeg decoders for the Android NDK.

Source archives and exact hashes are recorded in sources.lock.json. No host
library is linked into Android output. Generated build trees stay under work/.
"""
from pathlib import Path
import argparse
import concurrent.futures
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tarfile
import urllib.request

HERE = Path(__file__).resolve().parent
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
WORKSPACE = HERE.parents[3]
ABI_TARGETS = {
    'arm64-v8a': ('aarch64-linux-android26', 'aarch64', 'aarch64'),
    'armeabi-v7a': ('armv7a-linux-androideabi26', 'arm', 'armv7'),
    'x86': ('i686-linux-android26', 'x86', 'i686'),
    'x86_64': ('x86_64-linux-android26', 'x86_64', 'x86_64'),
}


def run(args, log, env):
    args = [str(arg) for arg in args]
    with log.open('a', encoding='utf-8') as output:
        output.write('\n' + subprocess.list2cmdline(args) + '\n')
        output.flush()
        result = subprocess.run(args, stdout=output, stderr=subprocess.STDOUT, env=env)
    if result.returncode:
        print(log.read_text(encoding='utf-8', errors='replace')[-8000:], flush=True)
        raise RuntimeError(f'Build command failed ({result.returncode}): {args[0]}; see {log}')


def prepare_sources(cache):
    source_root = HERE / 'work/sources'
    source_root.mkdir(parents=True, exist_ok=True)
    archives = HERE / 'work/archives'
    archives.mkdir(exist_ok=True)
    results = {}
    for entry in json.loads((HERE / 'sources.lock.json').read_text(encoding='utf-8')):
        filename = Path(entry['file']).name
        candidates = ([cache / filename] if cache else []) + [HERE / entry['file'], archives / filename]
        archive = next((path for path in candidates if path.is_file()), archives / filename)
        if not archive.is_file():
            with urllib.request.urlopen(entry['url'], timeout=120) as response, archive.open('wb') as target:
                shutil.copyfileobj(response, target)
        with archive.open('rb') as stream:
            digest = hashlib.file_digest(stream, 'sha256').hexdigest()
        if digest != entry['sha256']:
            raise RuntimeError(f'Archive checksum mismatch: {filename}')
        with tarfile.open(archive) as source:
            root_name = Path(source.getnames()[0]).parts[0]
            target = source_root / root_name
            if not target.is_dir():
                source.extractall(source_root, filter='data')
        name = 'gme' if 'game-music-emu' in filename else 'openmpt' if 'openmpt' in filename else 'dav1d'
        results[name] = target
    return results


def export_notices(sources):
    destination = HERE.parents[1] / 'app/src/main/assets/licenses/external'
    destination.mkdir(parents=True, exist_ok=True)
    files = [
        ('gme', 'license.txt', 'Game_Music_Emu-LGPL-2.1.txt'),
        ('gme', 'readme.txt', 'Game_Music_Emu-upstream-readme.txt'),
        ('gme', 'gme/ext/LICENSE', 'Game_Music_Emu-emu2413-MIT.txt'),
        ('dav1d', 'COPYING', 'dav1d-COPYING.txt'),
        ('openmpt', 'LICENSE', 'libopenmpt-LICENSE.txt'),
        ('openmpt', 'src/mpt/LICENSE.BSL-1.0.txt', 'libopenmpt-mpt-BSL-1.0.txt'),
        ('openmpt', 'include/minimp3/LICENSE', 'libopenmpt-minimp3-LICENSE.txt'),
    ]
    manifest = []
    for library, relative, name in files:
        data = (sources[library] / relative).read_bytes()
        (destination / name).write_bytes(data)
        manifest.append({'library': library, 'upstream_file': relative, 'asset': name,
                         'sha256': hashlib.sha256(data).hexdigest(), 'copy': 'complete original file'})
    relative = 'include/stb_vorbis/stb_vorbis.c'
    data = (sources['openmpt'] / relative).read_bytes()
    start = data.rfind(b'/*\n------------------------------------------------------------------------------\nThis software is available under 2 licenses')
    if start < 0:
        raise RuntimeError('Pinned stb_vorbis license block not found.')
    notice = data[start:]
    name = 'libopenmpt-stb_vorbis-LICENSE.txt'
    (destination / name).write_bytes(notice)
    manifest.append({'library': 'openmpt', 'upstream_file': relative, 'asset': name,
                     'sha256': hashlib.sha256(notice).hexdigest(), 'copy': 'complete final original license comment'})
    (HERE / 'notices.lock.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')


def source_versions(sources):
    gme_header = (sources['gme'] / 'gme/gme.h').read_text(encoding='utf-8')
    gme = re.search(r'/\* Game_Music_Emu ([0-9]+\.[0-9]+\.[0-9]+) \*/', gme_header).group(1)
    openmpt_mk = (sources['openmpt'] / 'libopenmpt/libopenmpt_version.mk').read_text(encoding='utf-8')
    values = dict(re.findall(r'LIBOPENMPT_VERSION_(\w+)=(.*)', openmpt_mk))
    openmpt = '.'.join(values[key].strip() for key in ['MAJOR', 'MINOR', 'PATCH']) + values['PREREL'].strip()
    return {'gme': gme, 'openmpt': openmpt}


def build_openmpt_project(source):
    project = HERE / 'work/openmpt-cmake'
    project.mkdir(exist_ok=True)
    android_mk = (source / 'build/android_ndk/Android.mk').read_text(encoding='utf-8')
    files = list(dict.fromkeys(re.findall(r'(?<![\w/])((?:common|libopenmpt|soundlib|sounddsp|include)/[\w./-]+\.(?:cpp|c))', android_mk)))
    if not files or not all((source / name).is_file() for name in files):
        raise RuntimeError('The pinned OpenMPT Android source list is incomplete.')
    cmake = [
        'cmake_minimum_required(VERSION 3.22)', 'project(timerchine_openmpt LANGUAGES C CXX)',
        'set(CMAKE_C_STANDARD 17)', 'set(CMAKE_CXX_STANDARD 23)',
        'set(CMAKE_POSITION_INDEPENDENT_CODE ON)', 'add_library(openmpt STATIC',
        *['  "' + (source / name).as_posix() + '"' for name in files], ')',
        'target_compile_definitions(openmpt PRIVATE LIBOPENMPT_BUILD MPT_WITH_ZLIB MPT_WITH_MINIMP3 MPT_WITH_STBVORBIS)',
        'target_compile_options(openmpt PRIVATE $<$<COMPILE_LANGUAGE:CXX>:-fexceptions;-frtti> -fvisibility=hidden)',
        'target_include_directories(openmpt PRIVATE',
        *['  "' + (source / name).as_posix() + '"' for name in ['', 'src', 'common', 'include']], ')',
        'install(TARGETS openmpt ARCHIVE DESTINATION lib)',
    ]
    (project / 'CMakeLists.txt').write_text('\n'.join(cmake) + '\n', encoding='utf-8')
    return project


def pc(prefix, filename, name, version, libs, private=''):
    folder = prefix / 'lib/pkgconfig'
    folder.mkdir(parents=True, exist_ok=True)
    text = f'prefix={prefix.as_posix()}\nlibdir=${{prefix}}/lib\nincludedir=${{prefix}}/include\n\nName: {name}\nDescription: {name} Android static decoder\nVersion: {version}\nLibs: -L${{libdir}} {libs}\nLibs.private: {private}\nCflags: -I${{includedir}}\n'
    (folder / filename).write_text(text, encoding='utf-8')


def verify_link(abi, args, prefix, build_root, log, env):
    names = ['libgme.a', 'libopenmpt.a', 'libdav1d.a']
    if not all((prefix / 'lib' / name).is_file() for name in names):
        return False
    source = build_root / 'link-check.c'
    source.write_text('#include <gme/gme.h>\n#include <libopenmpt/libopenmpt.h>\n#include <dav1d/dav1d.h>\n'
                      'int main(void) { Music_Emu *music = gme_new_emu(gme_spc_type, 44100); '
                      'if (music) gme_delete(music); Dav1dSettings settings; Dav1dContext *ctx = 0; '
                      'dav1d_default_settings(&settings); if (!dav1d_open(&ctx, &settings)) dav1d_close(&ctx); '
                      'return openmpt_get_library_version() ? 0 : 1; }\n', encoding='utf-8')
    host = 'windows-x86_64' if os.name == 'nt' else 'linux-x86_64'
    compiler = args.ndk / 'toolchains/llvm/prebuilt' / host / 'bin' / ('clang.exe' if os.name == 'nt' else 'clang')
    output = build_root / 'link-check'
    run([compiler, '--target=' + ABI_TARGETS[abi][0], '-fPIE', '-pie', '-I' + str(prefix / 'include'),
         source, '-L' + str(prefix / 'lib'), '-Wl,-z,max-page-size=16384', '-Wl,--start-group',
         '-lgme', '-lopenmpt', '-ldav1d', '-lc++_static', '-lc++abi', '-lunwind', '-Wl,--end-group',
         '-lz', '-lm', '-ldl', '-o', output], log, env)
    data = output.read_bytes()
    machine = int.from_bytes(data[18:20], 'little')
    expected = {'arm64-v8a': 183, 'armeabi-v7a': 40, 'x86': 3, 'x86_64': 62}[abi]
    if data[:4] != b'\x7fELF' or machine != expected:
        raise RuntimeError(f'Unexpected link-check output architecture: {abi}, {machine}')
    return True


def build_abi(abi, args, sources, openmpt_project):
    prefix = (args.prefix / abi).resolve()
    prefix.mkdir(parents=True, exist_ok=True)
    build_root = HERE / 'work' / abi
    build_root.mkdir(exist_ok=True)
    log = build_root / 'build.log'
    env = os.environ.copy()
    temp = build_root / 'tmp'
    temp.mkdir(exist_ok=True)
    for name in ['TEMP', 'TMP', 'TMPDIR']:
        env[name] = str(temp)
    if os.name == 'nt':
        # Avoid unrelated Conda/MSYS patch/pkg-config executables with DLLs
        # incompatible with this Python/NDK toolchain. No global PATH is changed.
        windows = Path(env.get('SystemRoot', 'C:/Windows'))
        env['PATH'] = os.pathsep.join(map(str, [args.cmake.parent, Path(sys.executable).parent,
                                               windows / 'System32', windows]))
    else:
        env['PATH'] = str(args.cmake.parent) + os.pathsep + env.get('PATH', '')
    if args.meson_tools:
        env['PYTHONPATH'] = str(args.meson_tools) + os.pathsep + env.get('PYTHONPATH', '')
    toolchain = args.ndk / 'build/cmake/android.toolchain.cmake'
    versions = source_versions(sources)
    common = [args.cmake, '-G', 'Ninja', '-DCMAKE_MAKE_PROGRAM=' + str(args.cmake.parent / ('ninja.exe' if os.name == 'nt' else 'ninja')),
              '-DCMAKE_TOOLCHAIN_FILE=' + str(toolchain), '-DANDROID_ABI=' + abi, '-DANDROID_PLATFORM=android-26',
              '-DANDROID_STL=c++_static', '-DCMAKE_BUILD_TYPE=Release', '-DCMAKE_POSITION_INDEPENDENT_CODE=ON',
              '-DCMAKE_INSTALL_PREFIX=' + str(prefix), '-DCMAKE_INSTALL_LIBDIR=lib']
    if 'gme' in args.libraries:
        build = build_root / 'gme'
        run([*common, '-S', sources['gme'], '-B', build, '-DGME_BUILD_SHARED=OFF', '-DGME_BUILD_STATIC=ON',
             '-DGME_BUILD_TESTING=OFF', '-DGME_BUILD_EXAMPLES=OFF', '-DBUILD_TESTING=OFF', '-DGME_YM2612_EMU=Nuked', '-DGME_ZLIB=ON'], log, env)
        run([args.cmake, '--build', build, '--parallel', args.jobs], log, env)
        run([args.cmake, '--install', build], log, env)
        pc(prefix, 'libgme.pc', 'Game_Music_Emu', versions['gme'], '-lgme', '-lz -lm -lc++_static -lc++abi -lunwind -ldl')
        print('READY', abi, 'gme', flush=True)
    if 'openmpt' in args.libraries:
        build = build_root / 'openmpt'
        run([*common, '-S', openmpt_project, '-B', build], log, env)
        run([args.cmake, '--build', build, '--parallel', args.jobs], log, env)
        run([args.cmake, '--install', build], log, env)
        includes = prefix / 'include/libopenmpt'
        includes.mkdir(parents=True, exist_ok=True)
        for header in (sources['openmpt'] / 'libopenmpt').iterdir():
            if header.suffix in ['.h', '.hpp']:
                shutil.copy2(header, includes / header.name)
        pc(prefix, 'libopenmpt.pc', 'libopenmpt', versions['openmpt'], '-lopenmpt', '-lz -lm -lc++_static -lc++abi -lunwind -ldl')
        print('READY', abi, 'openmpt', flush=True)
    if 'dav1d' in args.libraries:
        target, family, cpu = ABI_TARGETS[abi]
        host = 'windows-x86_64' if os.name == 'nt' else 'linux-x86_64'
        binaries = args.ndk / 'toolchains/llvm/prebuilt' / host / 'bin'
        ext = '.exe' if os.name == 'nt' else ''
        cross = build_root / 'dav1d-cross.ini'
        cross.write_text("[binaries]\nc = ['" + (binaries / ('clang' + ext)).as_posix() + "', '--target=" + target + "']\nar = '" + (binaries / ('llvm-ar' + ext)).as_posix() + "'\nstrip = '" + (binaries / ('llvm-strip' + ext)).as_posix() + "'\n[host_machine]\nsystem = 'android'\ncpu_family = '" + family + "'\ncpu = '" + cpu + "'\nendian = 'little'\n[properties]\nneeds_exe_wrapper = true\n[built-in options]\nc_args = ['-fPIC']\nc_link_args = ['-Wl,-z,max-page-size=16384']\n", encoding='utf-8')
        build = build_root / 'dav1d'
        meson = [sys.executable, '-m', 'mesonbuild.mesonmain']
        if not (build / 'build.ninja').is_file():
            run([*meson, 'setup', build, sources['dav1d'], '--cross-file', cross, '--prefix', prefix,
                 '--libdir=lib', '--default-library=static', '--buildtype=release', '-Denable_asm=false',
                 '-Denable_tools=false', '-Denable_examples=false', '-Denable_tests=false', '-Denable_docs=false', '-Db_staticpic=true'], log, env)
        run([*meson, 'compile', '-C', build, '-j', args.jobs], log, env)
        run([*meson, 'install', '-C', build, '--no-rebuild'], log, env)
        print('READY', abi, 'dav1d', flush=True)
    linked = verify_link(abi, args, prefix, build_root, log, env)
    report = {'abi': abi, 'ndk': args.ndk.name, 'api': 26, 'libraries': args.libraries, 'prefix': str(prefix),
              'all_library_link_check': linked,
              'archives': {path.name: hashlib.file_digest(path.open('rb'), 'sha256').hexdigest()
                           for path in (prefix / 'lib').glob('*.a')}}
    (build_root / 'result.json').write_text(json.dumps(report, indent=2), encoding='utf-8')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--ndk', type=Path, default=WORKSPACE / '.tools/android-sdk/ndk/28.2.13676358')
    parser.add_argument('--cmake', type=Path, default=WORKSPACE / '.tools/android-sdk/cmake/3.31.6/bin/cmake.exe')
    parser.add_argument('--meson-tools', type=Path, default=WORKSPACE / '.tools/meson-1.9.2')
    parser.add_argument('--source-cache', type=Path)
    parser.add_argument('--prefix', type=Path, default=HERE.parent / 'build-deps')
    parser.add_argument('--abis', nargs='+', choices=list(ABI_TARGETS), default=list(ABI_TARGETS))
    parser.add_argument('--libraries', nargs='+', choices=['gme', 'openmpt', 'dav1d'], default=['gme', 'openmpt', 'dav1d'])
    parser.add_argument('--jobs', type=int, default=3)
    parser.add_argument('--parallel-abis', type=int, default=2)
    args = parser.parse_args()
    args.ndk = args.ndk.resolve()
    args.cmake = args.cmake.resolve()
    sources = prepare_sources(args.source_cache)
    export_notices(sources)
    project = build_openmpt_project(sources['openmpt'])
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.parallel_abis) as pool:
        futures = [pool.submit(build_abi, abi, args, sources, project) for abi in args.abis]
        for future in futures:
            future.result()


if __name__ == '__main__':
    main()
