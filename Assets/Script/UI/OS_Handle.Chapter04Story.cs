public sealed partial class OS_Handle
{
    OSChapter04StoryDirector chapter04Story;
    public OSChapter04StoryDirector Chapter04Story => chapter04Story != null
        ? chapter04Story : (chapter04Story = GetComponent<OSChapter04StoryDirector>());
}
