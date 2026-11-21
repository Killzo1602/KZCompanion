import 'dart:async';

class DropImport {
  static final _paths = StreamController<DroppedFiles>.broadcast();
  static final _dragging = StreamController<bool>.broadcast();

  static Stream<DroppedFiles> get files => _paths.stream;
  static Stream<bool> get dragging => _dragging.stream;
  static void start() {}
}

class DroppedFiles {
  const DroppedFiles(this.paths, this.rejected);
  final List<String> paths;
  final List<String> rejected;
}
