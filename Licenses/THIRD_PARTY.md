# Сторонние материалы и условия

## Noto Sans Regular
- Файл: Assets/Diagnoz/Art/Fonts/NotoSans-Regular.ttf.
- Проект/авторы: Google, Noto project contributors (точные copyright notices в NotoSans-OFL.txt и name table шрифта).
- Источник: https://github.com/notofonts/noto-fonts/blob/main/hinted/ttf/NotoSans/NotoSans-Regular.ttf
- Лицензия: SIL Open Font License 1.1. Полный текст: NotoSans-OFL.txt.
- Исходный файл шрифта не изменён, планируется создание TMP-атласа локальным редактором.

## TextMeshPro Essential Resources / uGUI
- Автор: Unity Technologies; встроенный Liberation Sans имеет свои copyright notices.
- Источник: https://github.com/Unity-Technologies/uGUI/tree/6000.3/com.unity.ugui
- Пакет-источник: Package Resources/TMP Essential Resources.unitypackage.
- Условия uGUI/TMP: Unity Companion License для Unity-dependent projects.
  См. uGUI-License.md, Unity-Companion-License.txt и https://unity.com/legal/licenses/unity-companion-license.
- Liberation Sans: SIL OFL, включён полный исходный текст Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt.
- Неиспользуемые emoji/sprite ресурсы и HDRP-графы исключены; defaultSpriteAsset обнулён. Остальные включённые исходные .meta сохранены.
- Кириллица игрового UI использует Noto Sans, а не зависимость от установленного системного шрифта.

## Процедурная модель и игровой код
- Созданы в рамках этого ответа на основе согласованного кода из переписки; не взяты из Asset Store или сторонней библиотеки моделей.
- Исходник: SourceArt/build_sedan.py. Результат: JSON, OBJ, MTL; это стилизация по мотивам седана, а не точная копия ВАЗ-2106.
- Никаких эмблем или логотипов нет. Использование названия модели в описании не предоставляет прав на товарные знаки.
- Для самостоятельно созданных файлов комплекта дополнительных сторонних требований атрибуции не заявляется;
  это не юридическая гарантия исключительных прав на ИИ-созданные материалы.

## Пакеты
URP 17.3.0, Input System 1.17.0 и uGUI 2.0.0 указаны в manifest.json.
Их полные бинарные пакеты не включены: Unity Package Manager получает их из официального Registry.
Их собственные LICENSE/Third Party Notices доступны после импорта. Требуется действующая лицензия Unity.
