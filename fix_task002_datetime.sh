#!/bin/bash
# Fix TASK-002: Replace DateTimeOffset with DateTime to match acceptance criteria

echo "Fixing TASK-002 entity DateTime types..."

cd src/DataViewer.Domain/Entities

# Backup files first
for file in *.cs; do
    cp "$file" "$file.bak"
done

# Replace DateTimeOffset with DateTime in all entity files
for file in *.cs; do
    echo "Processing $file..."
    sed -i 's/DateTimeOffset?/DateTime?/g' "$file"
    sed -i 's/DateTimeOffset /DateTime /g' "$file"
    sed -i 's/DateTimeOffset>/DateTime>/g' "$file"
    sed -i 's/DateTimeOffset\.UtcNow/DateTime.UtcNow/g' "$file"
    sed -i 's/DateTimeOffset\.MinValue/DateTime.MinValue/g' "$file"
done

echo "Done! Changes made to:"
ls -1 *.cs

echo ""
echo "To verify changes:"
echo "  git diff src/DataViewer.Domain/Entities/"
echo ""
echo "To restore backups if needed:"
echo "  cd src/DataViewer.Domain/Entities && for f in *.cs.bak; do mv \$f \${f%.bak}; done"
