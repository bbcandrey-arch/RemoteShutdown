/// Версия/авторство приложения — держим в одном месте вместо разбросанных строк,
/// значение синхронизировано вручную с `version:` в pubspec.yaml (build-номер после
/// "+" туда не дублируем — он для сборок/стора, а не для показа пользователю).
class AppInfo {
  AppInfo._();

  static const String name = 'Выключатель ПК';
  static const String version = '1.1.0';
  static const String author = 'vol.and';
}
