/// Версия/авторство приложения — держим в одном месте вместо разбросанных строк,
/// значение синхронизировано вручную с `version:` в pubspec.yaml (build-номер после
/// "+" туда не дублируем — он для сборок/стора, а не для показа пользователю).
class AppInfo {
  AppInfo._();

  static const String name = 'Выключатель ПК';
  static const String version = '2.0.0';
  static const String author = 'vol.and';

  // TODO: заменить на настоящий адрес репозитория, когда он опубликован на
  // GitHub — та же ссылка используется и в трее Windows-агента (SettingsForm,
  // RepoUrl/UserGuideUrl). APK и инсталлятор публикуются в Releases того же репо.
  static const String repoUrl = 'https://github.com/USERNAME/remote_shutdown';
  static const String userGuideUrl = '$repoUrl/blob/main/docs/user-guide.md';
}
