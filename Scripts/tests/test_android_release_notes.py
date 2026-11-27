import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import android_release_notes as notes


class ReleaseNotesTests(unittest.TestCase):
    def test_only_feat_and_fix_become_notes(self):
        result = notes.notes_from_subjects([
            'feat(deployments): ejecutar los pasos largos desacoplados.',
            'ci: restringir permisos',
            'fix: corregir el scroll',
            'docs: actualizar README',
        ])
        self.assertEqual(result, '• Ejecutar los pasos largos desacoplados\n• Corregir el scroll')

    def test_fallback_without_user_facing_commits(self):
        self.assertEqual(notes.notes_from_subjects(['ci: algo', 'refactor: otra']), notes.FALLBACK)

    def test_caps_the_number_of_lines(self):
        result = notes.notes_from_subjects([f'feat: cambio {i}' for i in range(12)]).splitlines()
        self.assertEqual(len(result), notes.MAX_LINES + 1)
        self.assertTrue(result[-1].startswith('• …y 4'))

    def test_duplicates_are_listed_once(self):
        self.assertEqual(notes.notes_from_subjects(['fix: igual', 'fix(x): igual']), '• Igual')


if __name__ == '__main__':
    unittest.main()
