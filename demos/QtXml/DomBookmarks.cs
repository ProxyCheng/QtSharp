using System;
using System.IO;

// A port of Qt's examples/xml/dombookmarks: read an XBEL bookmarks file as a QDomDocument, fill a
// QTreeWidget from it, add a bookmark, and write the document back. The official jennifer.xbel is
// represented by the same structure with two entries so the test owns no external file.
[Demo("qtxml-dombookmarks")]
public static class DomBookmarks
{
    const string Xbel =
        "<?xml version='1.0' encoding='UTF-8'?>"
        + "<!DOCTYPE xbel>"
        + "<xbel version='1.0'>"
        + "  <bookmark href='https://www.qt.io/'><title>Qt</title></bookmark>"
        + "  <bookmark href='https://doc.qt.io/'><title>Qt Documentation</title></bookmark>"
        + "</xbel>";

    public static int Run(QApplication app)
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                failures++;
        }

        string dir = Path.Combine(Path.GetTempPath(), "qtsharp-xbel-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        try
        {
            using var document = new QDomDocument("xbel");
            Check(document.SetContent(Xbel, false), "QDomDocument parses the XBEL file");

            using var root = document.DocumentElement();
            Check(root.TagName() == "xbel", $"the document root is <xbel> ('{root.TagName()}')");

            using var bookmarks = document.ElementsByTagName("bookmark");
            Check(bookmarks.Count() == 2, $"the XBEL carries two bookmarks ({bookmarks.Count()})");

            // The official XbelTree::parseFolderItem walk: href from the element, title from its
            // <title> child, into a two-column QTreeWidget.
            using var tree = new QTreeWidget();
            tree.SetHeaderLabels(new[] { "Title", "Location" });
            for (int i = 0; i < bookmarks.Count(); i++)
            {
                using var node = bookmarks.Item(i);
                using var bookmark = node.ToElement();
                using var titleNode = bookmark.FirstChildElement("title");
                string title = titleNode.Text();
                string href = bookmark.Attribute("href");
                var item = new QTreeWidgetItem(tree);
                item.SetText(0, title);
                item.SetText(1, href);
            }
            Check(tree.TopLevelItemCount == 2, "the tree has one row per bookmark");
            var first = tree.TopLevelItem(0);
            Check(first.Text(0) == "Qt" && first.Text(1) == "https://www.qt.io/",
                $"the first row carries the title and href ('{first.Text(0)}', '{first.Text(1)}')");

            // XbelTree::addBookmark: a new <bookmark> under the root.
            using (var added = document.CreateElement("bookmark"))
            {
                added.SetAttribute("href", "https://github.com/ProxyCheng/QtSharp");
                using (var addedTitle = document.CreateElement("title"))
                {
                    using (var text = document.CreateTextNode("QtSharp"))
                        addedTitle.AppendChild(text);
                    added.AppendChild(addedTitle);
                }
                root.AppendChild(added);
            }

            using var reparsed = new QDomDocument("xbel");
            Check(reparsed.SetContent(document.ToString(), false), "the edited DOM serialises and re-parses");
            using var all = reparsed.ElementsByTagName("bookmark");
            Check(all.Count() == 3, $"the new bookmark is in the document ({all.Count()})");

            // XbelTree::save: QDomDocument::save through a QTextStream on a QFile.
            string output = Path.Combine(dir, "bookmarks.xbel");
            using (var file = new QFile(output))
            {
                Check(file.Open(QIODeviceBase.OpenMode.WriteOnly),
                    "the XBEL output file opens");
                using (var stream = new QTextStream(file))
                {
                    document.Save(stream, 4);
                    stream.Flush();
                }
                file.Close();
            }
            string saved = File.ReadAllText(output);
            Check(saved.Contains("QtSharp") && saved.Contains("github.com/ProxyCheng/QtSharp"),
                "the saved XBEL contains the new bookmark");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* temp files are harmless */ }
        }

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[FAIL] {failures} check(s) failed");
        return failures == 0 ? 0 : 1;
    }
}
