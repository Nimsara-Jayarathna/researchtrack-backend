# Validation is shared by bundle creation and the existing topic CLI.
def text: type == "string" and length > 0 and (test("[\\r\\n\\t]") | not);
def nullable_text: . == null or text;
def integer_between($min; $max): type == "number" and floor == . and . >= $min and . <= $max;
.schemaVersion == 1 and
(.topics | type == "array") and
(.topics | length > 0) and
([.topics[].name] | length == (unique | length)) and
all(.topics[];
  has("name") and has("domain") and has("description") and
  has("producerOwner") and has("consumerOwner") and has("consumerGroupId") and
  has("partitions") and has("replicationFactor") and has("retentionHours") and
  has("contractVersion") and has("contractReference") and has("approved") and
  (.name | text and length <= 249 and test("^researchtrack\\.[a-z][a-z0-9-]*\\.[a-z][a-z0-9-]*\\.v[1-9][0-9]*$|^researchtrack\\.deployment-smoke$")) and
  (.domain | text and test("^[a-z][a-z0-9-]*$")) and
  (. as $topic | .name == "researchtrack.deployment-smoke" and .domain == "deployment" or
    (.name | startswith("researchtrack." + $topic.domain + "."))) and
  (.description | text) and (.producerOwner | text) and
  (.consumerOwner | nullable_text) and (.consumerGroupId | nullable_text) and
  (.partitions | integer_between(1; 9999)) and .replicationFactor == 1 and
  (.retentionHours | integer_between(1; 99999)) and
  (.contractVersion | nullable_text) and (.contractReference | nullable_text) and
  (.approved | type == "boolean") and
  (if .approved then
    (.consumerOwner | text) and (.contractVersion | text) and (.contractReference | text)
   else true end)
)
