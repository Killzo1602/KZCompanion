import 'dart:convert';

import 'package:droiddeck/core/core.dart';
import 'package:shared_preferences/shared_preferences.dart';

const gridCols = 12;
const gridRows = 6;

enum GridType {
  cpu('CPU', 2, 2, 2, 3),
  gpu('GPU', 2, 2, 2, 3),
  ram('RAM', 2, 2, 2, 3),
  favorites('Favorites', 2, 2, 6, 3),
  audio('Audio controls', 4, 2, 5, 3),
  mixer('App volume', 4, 2, 5, 3),
  media('Now playing', 3, 2, 4, 3),
  claude('Claude Code usage', 3, 2, 4, 3),
  codex('Codex usage', 3, 2, 4, 3),
  clock('Clock', 2, 1, 3, 2);

  const GridType(this.label, this.minW, this.minH, this.w, this.h);
  final String label;
  final int minW, minH, w, h;
}

class GridItem {
  const GridItem(this.type, this.x, this.y, this.w, this.h);
  final GridType type;
  final int x, y, w, h;

  GridItem copyWith({int? x, int? y, int? w, int? h}) =>
      GridItem(type, x ?? this.x, y ?? this.y, w ?? this.w, h ?? this.h);

  bool overlaps(GridItem o) => x < o.x + o.w && o.x < x + w && y < o.y + o.h && o.y < y + h;

  bool get inside =>
      x >= 0 && y >= 0 && w >= type.minW && h >= type.minH && x + w <= gridCols && y + h <= gridRows;

  Map<String, Object> toJson() => {'type': type.name, 'x': x, 'y': y, 'w': w, 'h': h};

  @override
  bool operator ==(Object other) =>
      other is GridItem && other.type == type && other.x == x && other.y == y && other.w == w && other.h == h;

  @override
  int get hashCode => Object.hash(type, x, y, w, h);
}

bool gridFits(List<GridItem> items, GridItem item) =>
    item.inside && items.every((o) => o.type == item.type || !o.overlaps(item));

GridItem? gridFreeSpot(List<GridItem> items, GridType type) {
  for (var h = type.h; h >= type.minH; h--) {
    for (var w = type.w; w >= type.minW; w--) {
      for (var y = 0; y + h <= gridRows; y++) {
        for (var x = 0; x + w <= gridCols; x++) {
          final item = GridItem(type, x, y, w, h);
          if (gridFits(items, item)) return item;
        }
      }
    }
  }
  return null;
}

const gridStart = [
  GridItem(GridType.cpu, 0, 0, 2, 3),
  GridItem(GridType.gpu, 2, 0, 2, 3),
  GridItem(GridType.ram, 4, 0, 3, 3),
  GridItem(GridType.favorites, 0, 3, 7, 3),
  GridItem(GridType.audio, 7, 0, 5, 3),
  GridItem(GridType.mixer, 7, 3, 5, 3),
];

String gridEncode(List<GridItem> items) => jsonEncode([for (final i in items) i.toJson()]);

List<GridItem>? gridDecode(String? raw) {
  if (raw == null) return null;
  try {
    final items = <GridItem>[];
    for (final e in (jsonDecode(raw) as List).whereType<Map>()) {
      final type = GridType.values.asNameMap()[e['type']];
      if (type == null || items.any((i) => i.type == type)) continue;
      final item = GridItem(type, e['x'] as int, e['y'] as int, e['w'] as int, e['h'] as int);
      if (gridFits(items, item)) items.add(item);
    }
    return items;
  } catch (_) {
    return null;
  }
}

class KzGrid {
  static const key = 'kz_grid';

  static final layout = signal<List<GridItem>?>(null);
  static final editing = signal(false);

  static Future<void> load() async {
    layout.value = gridDecode((await SharedPreferences.getInstance()).getString(key));
  }

  static Future<void> save(List<GridItem> items) async {
    layout.value = List.unmodifiable(items);
    await (await SharedPreferences.getInstance()).setString(key, gridEncode(items));
  }

  static Future<void> reset() async {
    layout.value = null;
    await (await SharedPreferences.getInstance()).remove(key);
  }
}
