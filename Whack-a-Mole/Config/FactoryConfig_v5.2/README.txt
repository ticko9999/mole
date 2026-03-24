地鼠工厂 配置导出版 v5.2

目录说明
- csv/: 每个工作表导出为一个 CSV 文件
- json/: 每个工作表导出为一个 JSON 文件
- export_index.json: 所有工作表、文件名、字段头、哈希和行列统计
- manifest.txt: 交付清单

约定
- 布尔值统一为 1 / 0
- 多值字段统一使用 | 分隔
- CSV 编码为 UTF-8 with BOM，便于 Excel 与程序两侧兼容
- JSON 中：
  - 若首行是完整且唯一的字段名，则使用 records 模式
  - 否则保留 rows 二维数组模式
