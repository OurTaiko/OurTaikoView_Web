import gzip
import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('publish_player', Path(__file__).with_name('publish-player.py'))
publisher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(publisher)


class PublishingTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.source = Path(self.directory.name)
        (self.source / 'Build').mkdir()
        names = ['Web.loader.js', 'Web.framework.js.unityweb', 'Web.wasm.unityweb', 'Web.data.unityweb']
        (self.source / 'index.html').write_text(' '.join(f'"Build/{name}"' for name in names))
        for name in names:
            (self.source / 'Build' / name).write_bytes(gzip.compress(b'build') if name.endswith('unityweb') else b'loader')

    def test_manifest_is_last_and_only_after_all_cdn_checks(self):
        events = []
        publisher.publish(self.source, 'bucket', 'https://cdn.example', execute=True,
                          run=lambda args, **kw: events.append(('upload', args)),
                          verify=lambda *args: events.append(('verify', args)))
        self.assertEqual([kind for kind, _ in events], ['upload'] * 5 + ['verify'] * 5 + ['upload'])
        final = events[-1][1]
        self.assertIn('s3://bucket/player-build.json', final)
        self.assertIn('no-store', final)
        wasm = next(args for kind, args in events if kind == 'upload' and any('wasm.unityweb' in a for a in args))
        self.assertIn('application/wasm', wasm)
        self.assertIn('gzip', wasm)

    def test_upload_or_verification_failure_never_switches_manifest(self):
        for stage in ('upload', 'verify'):
            with self.subTest(stage=stage):
                uploads = []
                def run(args, **kwargs):
                    uploads.append(args)
                    if stage == 'upload':
                        raise subprocess.CalledProcessError(1, args)
                def verify(*args):
                    raise ValueError('CDN mismatch')
                with self.assertRaises((subprocess.CalledProcessError, ValueError)):
                    publisher.publish(self.source, 'bucket', 'https://cdn.example', execute=True, run=run, verify=verify)
                self.assertFalse(any('s3://bucket/player-build.json' in args for args in uploads))

    def test_missing_asset_is_rejected_before_upload(self):
        (self.source / 'Build/Web.wasm.unityweb').unlink()
        with self.assertRaisesRegex(ValueError, 'Missing'):
            publisher.make_manifest(self.source)

    def test_lazy_audio_decoder_assets_are_required_and_hashed(self):
        index = self.source / 'index.html'
        index.write_text(index.read_text() + '<script src="audio/decode.js"></script>')
        assets = ['decode.js', 'ogg-worker.js', 'vendor/ogg-vorbis-decoder.min.js',
                  'vendor/ogg-opus-decoder.min.js']
        for name in assets:
            with self.assertRaisesRegex(ValueError, 'Missing audio decoder asset'):
                publisher.make_manifest(self.source)
            path = self.source / 'audio' / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text('// decoder')
        manifest = publisher.make_manifest(self.source)
        for name in assets:
            self.assertIn('audio/' + name, manifest['files'])

    def test_content_changes_create_a_new_version(self):
        before = publisher.make_manifest(self.source)
        (self.source / 'Build/Web.loader.js').write_bytes(b'new loader')
        self.assertNotEqual(before['path'], publisher.make_manifest(self.source)['path'])


if __name__ == '__main__':
    unittest.main()
