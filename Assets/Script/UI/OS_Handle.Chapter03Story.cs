public sealed partial class OS_Handle
{
    OSChapter03StoryDirector chapter03Story;
    public OSChapter03StoryDirector Chapter03Story => chapter03Story != null ? chapter03Story
        : (chapter03Story = GetComponent<OSChapter03StoryDirector>());
    public bool IsChapter03StoryMessageProtected => Chapter03Story?.IsMessageProtected == true;
    public bool IsChapter03StoryProtected => Chapter03Story?.IsCinematic == true;
}
