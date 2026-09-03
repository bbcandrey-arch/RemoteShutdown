import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';

import 'app_info.dart';

/// Краткая справка + ссылка на полную инструкцию (docs/user-guide.md) — доступна с
/// самого первого экрана (сопряжение), а не только из меню Dashboard: пока телефон не
/// сопряжён с ПК, до Dashboard человек ещё не добрался, а помощь "как развернуть агент
/// и организовать сопряжение" нужна именно на этом шаге. См. такую же вкладку "Помощь"
/// в настройках Windows-агента (SettingsForm.cs).
void showHelpDialog(BuildContext context) {
  showDialog<void>(
    context: context,
    builder: (ctx) => AlertDialog(
      title: const Text('Справка'),
      content: const SingleChildScrollView(
        child: Text(
          'Как развернуть агент на ПК\n'
          '1. Скачайте и установите Windows-агент (ссылка на ПК — «Полная инструкция» ниже).\n'
          '2. Один раз потребуются права администратора (UAC) — агент ставится как служба '
          'Windows и работает в фоне, даже если в систему ещё никто не вошёл.\n'
          '3. После установки в трее (у часов) появится значок агента — дважды кликните, '
          'откроются настройки с QR-кодом и PIN.\n\n'
          'Как подключить телефон\n'
          '1. На этом экране нажмите «Сканировать QR-код» и наведите камеру на экран ПК — '
          'все поля заполнятся сами.\n'
          '2. Либо введите вручную: IP-адрес и порт ПК (видны там же, в настройках агента) '
          'и PIN (кнопка «Показать»).\n'
          '3. Телефон и ПК должны быть в одной Wi-Fi сети.\n\n'
          'Не получается подключиться\n'
          '• Проверьте, что сеть на телефоне и ПК одна и та же (не гостевая).\n'
          '• На ПК: вкладка «Общие» → «Добавить правило в брандмауэр».\n'
          '• Убедитесь, что значок агента есть в трее Windows (иногда прячется под '
          'стрелочкой «Показать скрытые значки»).',
        ),
      ),
      actions: [
        TextButton(onPressed: () => Navigator.pop(ctx), child: const Text('Закрыть')),
        FilledButton(
          onPressed: () {
            Navigator.pop(ctx);
            launchUrl(Uri.parse(AppInfo.userGuideUrl), mode: LaunchMode.externalApplication);
          },
          child: const Text('Полная инструкция'),
        ),
      ],
    ),
  );
}
