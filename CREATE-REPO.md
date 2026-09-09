# Creating the GitHub repository

Run from the repository root, once the scaffold is committed.

## With the GitHub CLI

    gh repo create koma-desktop \
      --public \
      --source=. \
      --remote=origin \
      --push \
      --description "Cross-platform desktop library manager, reader and editor for KOMA publications. C# and Avalonia, Windows and Linux. Targets the KOMA 0.9 Reading System and Authoring Tool conformance classes."

    gh repo edit --add-topic koma \
                 --add-topic comics \
                 --add-topic manga \
                 --add-topic bande-dessinee \
                 --add-topic ebook \
                 --add-topic ebook-reader \
                 --add-topic comic-reader \
                 --add-topic file-format \
                 --add-topic csharp \
                 --add-topic dotnet \
                 --add-topic avalonia \
                 --add-topic avaloniaui \
                 --add-topic cross-platform \
                 --add-topic desktop-app \
                 --add-topic library-manager \
                 --add-topic cbz

## Without the CLI

Create an empty repository on github.com, paste the description above into the
About panel, add the topics from the list above, then:

    git init -b main
    git add -A
    git commit -m "Initial scaffold"
    git remote add origin git@github.com:<you>/koma-desktop.git
    git push -u origin main

## Notes

Topics must be lowercase, may contain hyphens, and GitHub allows at most 20 per
repository. The 16 above leave room.

`bande-dessinee` has no accent because GitHub topics are ASCII only.

The description is a single line and GitHub truncates it in search listings
around 150 characters, so the important part is at the front.

Consider adding `koma-format` upstream as a topic on the specification
repository too, so the two are discoverable from each other.
