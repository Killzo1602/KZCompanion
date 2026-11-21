import 'dart:async';

class DropImport {
  static final _paths = StreamController<DroppedFiles>.broadcast();
  static final _dragging = StreamController<bool>.broadcast();

  static Stream<DroppedFiles> get files => _paths.stream;
  static Stream<bool> get dragging => _dragging.stream;
  static void start() {}
}

class DroppedFiles {
  const DroppedFiles(this.paths, this.rejected, [this.unresolved = const []]);
  final List<String> paths;
  final List<String> rejected;

  /// Itens soltos sem caminho de arquivo (ex.: apps do Menu Iniciar): so o nome; abre o seletor.
  final List<String> unresolved;
}
