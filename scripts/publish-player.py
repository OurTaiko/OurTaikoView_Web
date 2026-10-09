#!/usr/bin/env python3
"""Publish an immutable Web build, verify it through CloudFront, then switch the manifest.

Preview: python3 scripts/publish-player.py
Publish: python3 scripts/publish-player.py --publish [--profile AWS_PROFILE]
Uses existing AWS CLI credentials; never writes credentials or changes bucket policies.
"""

import argparse
import hashlib
import json
import mimetypes
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import urllib.request


def metadata(path):
    name = path.name
    encoding = None
    if name.endswith('.unityweb'):
        if path.read_bytes()[:2] != b'\x1f\x8b':
            raise ValueError(f'{name}: expected this project\'s Gzip .unityweb output')
        name = name.removesuffix('.unityweb')
        encoding = 'gzip'
    elif name.endswith(('.gz', '.br')):
        name, suffix = name.rsplit('.', 1)
        encoding = 'gzip' if suffix == 'gz' else 'br'
    content_type = {
        '.wasm': 'application/wasm', '.js': 'application/javascript',
        '.data': 'application/octet-stream', '.html': 'text/html; charset=utf-8',
    }.get(Path(name).suffix) or mimetypes.guess_type(name)[0] or 'application/octet-stream'
    return content_type, encoding


def make_manifest(source):
    index = source / 'index.html'
    if not index.is_file():
        raise ValueError('Build View_Web first: missing Builds/Web/index.html')
    # Validate the template's concrete build references before publishing anything.
    references = re.findall(r'[\'\"](Build/[^\'\"]+)[\'\"]', index.read_text())
    if len(set(references)) < 4:
        raise ValueError('Expected HTML references to loader, framework, WASM and data')
    for reference in references:
        if '..' in Path(reference).parts or not (source / reference).is_file():
            raise ValueError(f'Missing or invalid build reference: {reference}')
    files = {}
    for path in sorted(source.rglob('*')):
        if not path.is_file() or path.name == '.DS_Store':
            continue
        metadata(path)
        files[path.relative_to(source).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    build_id = hashlib.sha256(json.dumps(files, sort_keys=True).encode()).hexdigest()[:16]
    return {'path': f'/player/{build_id}/index.html', 'files': files}


def verify_cdn(url, expected_hash, content_type, encoding):
    request = urllib.request.Request(url, headers={'Accept-Encoding': 'identity'})
    with urllib.request.urlopen(request, timeout=120) as response:
        # urllib leaves encoded bodies intact, so compare with the uploaded bytes.
        digest = hashlib.sha256()
        while chunk := response.read(1024 * 1024):
            digest.update(chunk)
        if digest.hexdigest() != expected_hash:
            raise ValueError(f'CloudFront content mismatch: {url}')
        if response.headers.get_content_type() != content_type.split(';')[0]:
            raise ValueError(f'CloudFront Content-Type mismatch: {url}')
        if response.headers.get('Content-Encoding') != encoding:
            raise ValueError(f'CloudFront Content-Encoding mismatch: {url}')


def publish(source, bucket, cdn_url, profile=None, execute=False, run=None, verify=None):
    run = run or subprocess.run
    verify = verify or verify_cdn
    manifest = make_manifest(source)
    prefix = manifest['path'].rsplit('/', 1)[0].lstrip('/')
    print(f'Version: {prefix}\nEntry: {cdn_url}/{manifest["path"].lstrip("/")}')
    if not execute:
        print(f'Preview only: {len(manifest["files"])} files -> s3://{bucket}/{prefix}/')
        print('After CDN verification, replace player-build.json. Use --publish to upload.')
        return manifest
    aws = ['aws'] + (['--profile', profile] if profile else [])
    for name, checksum in manifest['files'].items():
        path = source / name
        content_type, encoding = metadata(path)
        args = aws + ['s3', 'cp', str(path), f's3://{bucket}/{prefix}/{name}',
                      '--only-show-errors', '--content-type', content_type,
                      '--cache-control', 'public,max-age=31536000,immutable',
                      '--metadata', f'sha256={checksum}']
        if encoding:
            args += ['--content-encoding', encoding]
        run(args, check=True)
    # Never expose the new version until every object is accessible and byte-identical.
    for name, checksum in manifest['files'].items():
        verify(f'{cdn_url}/{prefix}/{name}', checksum, *metadata(source / name))
    with tempfile.TemporaryDirectory(prefix='ourtaiko-manifest-') as directory:
        path = Path(directory) / 'player-build.json'
        path.write_text(json.dumps(manifest, indent=2) + '\n')
        run(aws + ['s3', 'cp', str(path), f's3://{bucket}/player-build.json',
                   '--only-show-errors', '--content-type', 'application/json',
                   '--cache-control', 'no-store'], check=True)
    print(f'Published: {cdn_url}/player-build.json')
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=Path(__file__).resolve().parents[1] / 'Builds/Web')
    parser.add_argument('--bucket', default='ourtaiko-public')
    parser.add_argument('--cdn-url', default='https://d2mguycu233w0q.cloudfront.net')
    parser.add_argument('--profile', help='Existing AWS CLI profile')
    parser.add_argument('--publish', action='store_true', help='Upload files and switch the live manifest')
    args = parser.parse_args()
    if args.publish and not shutil.which('aws'):
        parser.error('AWS CLI is not installed. Install AWS CLI v2 and configure an upload profile first.')
    # Snapshot before hashing/uploading so a later local build cannot change this release.
    with tempfile.TemporaryDirectory(prefix='ourtaiko-player-') as directory:
        source = Path(directory) / 'Web'
        shutil.copytree(args.source, source, ignore=shutil.ignore_patterns('.DS_Store'))
        publish(source, args.bucket, args.cdn_url.rstrip('/'), args.profile, args.publish)


if __name__ == '__main__':
    main()
