import 'package:flutter/material.dart';

/// Тема приложения по Material Design 3 (см. скилл mobile-android-design):
/// цвета — только через `ColorScheme` (автоматическая поддержка тёмной темы),
/// формы/типографика/отступы — через темы компонентов, а не точечно на
/// каждом виджете. Один источник правды для light/dark вместо `ThemeData()`
/// по умолчанию, как было раньше.
class AppTheme {
  AppTheme._();

  static const _seed = Color(0xFF2B5CA8); // тот же синий, что и в иконке приложения

  static ThemeData light() => _build(Brightness.light);
  static ThemeData dark() => _build(Brightness.dark);

  static ThemeData _build(Brightness brightness) {
    final scheme = ColorScheme.fromSeed(seedColor: _seed, brightness: brightness);

    return ThemeData(
      useMaterial3: true,
      colorScheme: scheme,
      scaffoldBackgroundColor: scheme.surface,

      appBarTheme: AppBarTheme(
        backgroundColor: scheme.surface,
        foregroundColor: scheme.onSurface,
        surfaceTintColor: scheme.surfaceTint,
        centerTitle: false,
        elevation: 0,
      ),

      cardTheme: CardThemeData(
        elevation: 0,
        color: scheme.surfaceContainerHigh,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
        margin: EdgeInsets.zero,
      ),

      listTileTheme: ListTileThemeData(
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
        iconColor: scheme.onSurfaceVariant,
      ),

      chipTheme: ChipThemeData(
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
        side: BorderSide.none,
        backgroundColor: scheme.secondaryContainer,
        labelStyle: TextStyle(color: scheme.onSecondaryContainer),
      ),

      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20)),
          padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 14),
        ),
      ),

      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20)),
          padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 14),
        ),
      ),

      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: scheme.surfaceContainerHighest,
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(12),
          borderSide: BorderSide.none,
        ),
        contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
      ),

      dialogTheme: DialogThemeData(
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(24)),
      ),

      floatingActionButtonTheme: FloatingActionButtonThemeData(
        backgroundColor: scheme.primaryContainer,
        foregroundColor: scheme.onPrimaryContainer,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
      ),
    );
  }
}

/// M3 не определяет "успех"/"тепло" из коробки (только primary/secondary/
/// tertiary/error) — статус "ПК онлайн" по смыслу ближе к успеху, чем к
/// primary/tertiary, поэтому заводим свою пару container/onContainer в той же
/// тональной логике, что и остальная схема (а не сырой Colors.green, как было
/// раньше — тот не адаптируется под тёмную тему).
extension AppStatusColors on ColorScheme {
  Color get onlineContainer => brightness == Brightness.light ? const Color(0xFFC8F0CE) : const Color(0xFF1F4B2A);
  Color get onOnlineContainer => brightness == Brightness.light ? const Color(0xFF0B3D18) : const Color(0xFFB6F2C0);
}
