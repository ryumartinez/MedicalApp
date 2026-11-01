using MedicalApp.Common.Domain.MediaAssets;
using MedicalApp.Common.Domain.ProgressForms;

namespace MedicalApp.Features.ProgressForms.CreateProgressForm;

public class CreateProgressFormSummary : Summary<CreateProgressFormEndpoint>
{
    public CreateProgressFormSummary()
    {
        Summary = "Create a form draft";
        Description =
            "Creates a draft owned by the calling doctor. A draft can be incomplete but must respect the limits: " +
            "title up to 200 characters, description and instructions up to 2,000, up to 50 questions, labels up to 500, " +
            "2 to 20 options of up to 100 characters each. Question types are Text, NumericScale (integer min, max, optional step " +
            "and endpoint labels), SingleChoice, Image (JPEG, PNG, WebP), and Video (MP4); each attachment question takes one file. " +
            "Each question has a stable key that identifies it across versions. Returns the form ID and its revision for later updates.";

        ExampleRequest = new CreateProgressFormRequest("Daily mood", "Answer each evening.",
        [
            new QuestionDto(null, "mood", QuestionType.NumericScale, "How was your mood today?", null, true, 0, 10, 1, "Very low", "Very good"),
            new QuestionDto(null, "sleep", QuestionType.SingleChoice, "How did you sleep?", null, true,
                Options: [new QuestionDto.OptionDto(null, "Poorly"), new QuestionDto.OptionDto(null, "Well")]),
            new QuestionDto(null, "notes", QuestionType.Text, "Anything else?", "Optional.", false),
            new QuestionDto(null, "exercise-1", QuestionType.Video, "Record exercise 1", null, false,
                AllowedContentTypes: [MediaAsset.Mp4])
        ]);
    }
}
