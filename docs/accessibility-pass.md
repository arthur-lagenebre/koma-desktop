# Two passes over the accessibility

Neither can be run from the code: one wants a screen reader talking, the other
wants eyes on a screen. Both are meant to be done in one sitting and written
down afterwards — the notes are the output, not the passing of them.

Write what was heard or seen under each step, and open an entry in `NEXT.md`
for anything that failed. A step nobody could finish is a result too.

## Pass one: with a screen reader running

Windows: Ctrl+Windows+Enter starts and stops the Narrator. Give it twenty
minutes, and **unplug the mouse** — not to be thorough, but because the hand
goes to it without asking.

The question under every step is not "did it say something" but "could someone
act on what it said".

### The shelf

1. Start the application. Does anything say what window opened, and what is in
   it?
2. Tab into the shelf. What does the first card announce? It should be one
   sentence — title, series, how far it was read — and not four fragments.
3. Walk with the arrows across a row, then down a row. Is each card announced
   as it is reached, and is it clear that the focus moved?
4. Tab to the search box and type two letters. Is the box announced by what it
   is for rather than by its content? Is anything said about the shelf having
   changed under it?
5. Reach the order list and change it. Does it say what the list is for, or
   only what it now holds?
6. Reach Resume, Folders, Import, and the language list. Does each say what it
   does?

Failures worth noting: a card that takes more than a breath to announce, a
control announced only by its value, a change nobody is told about.

### Reading

7. Open a publication from the shelf. Is it said that something opened, and
   what?
8. Listen to what the spread announces: where it stands, and what each page
   says of itself. Is "Not described" clear, or does it sound like a fault?
9. Turn a page. Is the new spread announced without asking?
10. Press Escape. Is it clear you are back on the shelf?

### A form

11. Open Edit on a publication. Are the tabs announced, and can they be
    reached and changed from the keyboard?
12. Walk the fields of the first tab. Does each say what it is for before it
    says what it holds?
13. In People, add a line, fill a name, take the line out again. Is the ✕
    button announced as something other than a symbol?
14. Save with a bad language tag — `français` — and listen. Is the refusal
    announced at all? If nothing is said, that is the most serious thing this
    pass can find: the message is on screen and nowhere else.

### The verdict to write down

- Which steps could be finished without looking at the screen.
- Which announcements were long enough to be tiring.
- Anything announced as "button" or "text" and nothing else.

## Pass two: contrast and focus

No screen reader; a light room, then the same with the system in light theme
and again in dark. The application follows the system, and the colours it sets
itself do not.

### What to measure

The figures are those of WCAG 2.2: **4.5:1** for text, **3:1** for anything
that is not text but carries meaning — a border that says "picked", a bar that
says "working". Any colour picker with a contrast readout will do; the Windows
Accessibility Insights tool has one built in.

Measure in **both themes**, since only the application's own colours are
fixed:

1. The red of a refusal in the edit and pages windows, over the form
   background. This one is suspect: it is a single fixed colour over a
   background that follows the system.
2. The grey of a missing cover on the shelf, and of a page not yet read in the
   pages window, against the panel behind them.
3. The blue border of a picked card, against the card and against the
   background.
4. The dimmed text — the explanatory lines under fields, at 55 to 75 percent
   opacity — over their background. Opacity is where contrast quietly goes.
5. The subtitle lines of a card, same reason.

### Focus

Mouse unplugged again, but no screen reader.

6. Tab from the top of the window to the bottom. Write down the order. Does it
   follow what the eye reads, or does it jump?
7. Is the focus **visible at every stop**? On a card, on a tab, in a text box,
   on the ✕ of a row, on the language list.
8. Open the Edit window. Where does the focus start? Does Escape close it, and
   does the focus come back to where it was on the shelf?
9. Open the pages window and take a page out. When the confirmation appears,
   where is the focus? Can it be answered without a mouse? Which button does
   Enter press — and is that the one that should be pressed by accident?
10. While a save is running the form is out of reach. Where does the focus go,
    and where does it come back to?

### The verdict to write down

- Every measured ratio, both themes, with the pair of colours.
- Every stop where the focus was invisible or the order surprising.
- Anything that cannot be reached at all without a mouse.
