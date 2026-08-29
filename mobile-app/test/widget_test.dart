import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:mobile_app/features/pairing/pairing_screen.dart';

void main() {
  // Тестируем PairingScreen напрямую, минуя _StartupGate из main.dart: тот дёргает
  // shared_preferences через платформенный канал, которого в widget-тесте нет,
  // а сам экран сопряжения от этого не зависит.
  testWidgets('Экран сопряжения показывает поля для IP и порта', (WidgetTester tester) async {
    await tester.pumpWidget(
      ProviderScope(
        child: MaterialApp(home: PairingScreen(onPaired: () {})),
      ),
    );

    expect(find.text('Подключение к ПК'), findsOneWidget);
    expect(find.text('IP-адрес ПК'), findsOneWidget);
    expect(find.text('Подключиться'), findsOneWidget);
  });
}
